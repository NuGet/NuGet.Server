using System.Text;
using System.Web;
using Ninject;
using NuGet.Resources;
using NuGet.Server.DataServices;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Web.Configuration;

namespace NuGet.Server.Infrastructure
{
    /// <summary>
    /// ServerPackageRepository represents a folder of nupkgs on disk. All packages are cached during the first request in order
    /// to correctly determine attributes such as IsAbsoluteLatestVersion. Adding, removing, or making changes to packages on disk 
    /// will clear the cache.
    /// </summary>
    public class ServerPackageRepository : PackageRepositoryBase, IServerPackageRepository, IPackageLookup, IDisposable
    {
        private IDictionary<IPackage, DerivedPackageData> _packages;
        private readonly object _lockObj = new object();
        private readonly IFileSystem _fileSystem;
        private readonly IPackagePathResolver _pathResolver;
        private readonly Func<string, bool, bool> _getSetting;
        private FileSystemWatcher _fileWatcher;
        private readonly string _filter = String.Format(CultureInfo.InvariantCulture, "*{0}", Constants.PackageExtension);
        private bool _monitoringFiles = false;
        private const string NupkgHashExtension = ".hash";
        private const string NupkgTempHashExtension = ".thash";

        public ServerPackageRepository(string path)
            : this(new DefaultPackagePathResolver(path), new PhysicalFileSystem(path))
        {

        }

        public ServerPackageRepository(IPackagePathResolver pathResolver, IFileSystem fileSystem, Func<string, bool, bool> getSetting = null)
        {
            if (pathResolver == null)
            {
                throw new ArgumentNullException("pathResolver");
            }

            if (fileSystem == null)
            {
                throw new ArgumentNullException("fileSystem");
            }

            _fileSystem = fileSystem;
            _pathResolver = pathResolver;
            _getSetting = getSetting ?? GetBooleanAppSetting;
        }

        [Inject]
        public IHashProvider HashProvider { get; set; }

        public override IQueryable<IPackage> GetPackages()
        {
            return PackageCache.Keys.AsQueryable<IPackage>();
        }

        public IQueryable<Package> GetPackagesWithDerivedData()
        {
            var cache = PackageCache;
            return cache.Keys.Select(p => new Package(p, cache[p])).AsQueryable();
        }

        public bool Exists(string packageId, SemanticVersion version)
        {
            return FindPackage(packageId, version) != null;
        }

        public IPackage FindPackage(string packageId, SemanticVersion version)
        {
            return FindPackagesById(packageId).Where(p => p.Version.Equals(version)).FirstOrDefault();
        }

        public IEnumerable<IPackage> FindPackagesById(string packageId)
        {
            return GetPackages().Where(p => StringComparer.OrdinalIgnoreCase.Compare(p.Id, packageId) == 0);
        }

        /// <summary>
        /// Gives the Package containing both the IPackage and the derived metadata.
        /// The returned Package will be null if <paramref name="package" /> no longer exists in the cache.
        /// </summary>
        public Package GetMetadataPackage(IPackage package)
        {
            Package metadata = null;

            // The cache may have changed, and the metadata may no longer exist
            DerivedPackageData data = null;
            if (PackageCache.TryGetValue(package, out data))
            {
                metadata = new Package(package, data);
            }

            return metadata;
        }

        public IQueryable<IPackage> Search(string searchTerm, IEnumerable<string> targetFrameworks, bool allowPrereleaseVersions)
        {
            var cache = PackageCache;

            var packages = cache.Keys.AsQueryable()
                .Find(searchTerm)
                .FilterByPrerelease(allowPrereleaseVersions);
            if (!EnableDelisting)
            {
                packages = packages.Where(p => p.Listed);
            }

            if (EnableFrameworkFiltering && targetFrameworks.Any())
            {
                // Get the list of framework names
                var frameworkNames = targetFrameworks.Select(frameworkName => VersionUtility.ParseFrameworkName(frameworkName));

                packages = packages.Where(package => frameworkNames.Any(frameworkName => VersionUtility.IsCompatible(frameworkName, cache[package].SupportedFrameworks)));
            }

            return packages.AsQueryable();
        }

        public IEnumerable<IPackage> GetUpdates(IEnumerable<IPackageName> packages, bool includePrerelease, bool includeAllVersions, IEnumerable<FrameworkName> targetFrameworks, IEnumerable<IVersionSpec> versionConstraints)
        {
            return this.GetUpdatesCore(packages, includePrerelease, includeAllVersions, targetFrameworks, versionConstraints);
        }

        public override string Source
        {
            get
            {
                return _fileSystem.Root;
            }
        }

        public override bool SupportsPrereleasePackages
        {
            get
            {
                return true;
            }
        }

        /// <summary>
        /// Add a file to the repository.
        /// </summary>
        public override void AddPackage(IPackage package)
        {
            string fileName = _pathResolver.GetPackageFileName(package);
            if (_fileSystem.FileExists(fileName) && !AllowOverrideExistingPackageOnPush)
            {
                throw new InvalidOperationException(String.Format(NuGetResources.Error_PackageAlreadyExists, package));
            }

            lock (_lockObj)
            {
                using (Stream stream = package.GetStream())
                {
                    _fileSystem.AddFile(fileName, stream);
                }

                InvalidatePackages();
            }
        }

        /// <summary>
        /// Unlist or delete a package
        /// </summary>
        public override void RemovePackage(IPackage package)
        {
            if (package != null)
            {
                string fileName = _pathResolver.GetPackageFileName(package);

                lock (_lockObj)
                {
                    if (EnableDelisting)
                    {
                        var fullPath = _fileSystem.GetFullPath(fileName);

                        if (File.Exists(fullPath))
                        {
                            File.SetAttributes(fullPath, File.GetAttributes(fullPath) | FileAttributes.Hidden);
                            // Delisted files can still be queried, therefore not deleting persisted hashes if present.
                            // Also, no need to flip hidden attribute on these since only the one from the nupkg is queried.
                        }
                        else
                        {
                            Debug.Fail("unable to find file");
                        }
                    }
                    else
                    {
                        _fileSystem.DeleteFile(fileName);
                        if (EnablePersistNupkgHash)
                        {
                            _fileSystem.DeleteFile(GetHashFile(fileName, false));
                            _fileSystem.DeleteFile(GetHashFile(fileName, true));
                        }
                    }

                    InvalidatePackages();
                }
            }
        }

        /// <summary>
        /// Remove a package from the respository.
        /// </summary>
        public void RemovePackage(string packageId, SemanticVersion version)
        {
            IPackage package = FindPackage(packageId, version);

            RemovePackage(package);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            DetachEvents();
        }

        /// <summary>
        /// *.nupkg files in the root folder
        /// </summary>
        private IEnumerable<string> GetPackageFiles()
        {
            // Check top level directory
            foreach (var path in _fileSystem.GetFiles(String.Empty, _filter))
            {
                yield return path;
            }
        }

        /// <summary>
        /// Internal package cache containing both the packages and their metadata. 
        /// This data is generated if it does not exist already.
        /// </summary>
        private IDictionary<IPackage, DerivedPackageData> PackageCache
        {
            get
            {
                lock (_lockObj)
                {
                    if (_packages == null)
                    {
                        if (!_monitoringFiles)
                        {
                            // attach events the first time
                            _monitoringFiles = true;
                            AttachEvents();
                        }

                        _packages = CreateCache();
                    }

                    return _packages;
                }
            }
        }

        /// <summary>
        /// Sets the current cache to null so it will be regenerated next time.
        /// </summary>
        public void InvalidatePackages()
        {
            lock (_lockObj)
            {
                _packages = null;
            }
        }

        private string GetHashFile(string pathToNupkg, bool isTempFile)
        {
            // path_to_nupkg\package.nupkg => path_to_nupkg\package.hash or path_to_nupkg\package.thash
            // reason for replacing extension instead of appending: elimination potential file-system file name length limits.
            if (string.IsNullOrEmpty(pathToNupkg))
            {
                return pathToNupkg;
            }
            return Path.ChangeExtension(pathToNupkg, isTempFile ? NupkgTempHashExtension : NupkgHashExtension);
        }

        /// <summary>
        /// CreateCache loads all packages and determines additional metadata such as the hash, IsAbsoluteLatestVersion, and IsLatestVersion.
        /// </summary>
        private IDictionary<IPackage, DerivedPackageData> CreateCache()
        {
            ConcurrentDictionary<IPackage, DerivedPackageData> packages = new ConcurrentDictionary<IPackage, DerivedPackageData>();

            ParallelOptions opts = new ParallelOptions();
            opts.MaxDegreeOfParallelism = 4;

            ConcurrentDictionary<string, Tuple<IPackage, DerivedPackageData>> absoluteLatest = new ConcurrentDictionary<string, Tuple<IPackage, DerivedPackageData>>();
            ConcurrentDictionary<string, Tuple<IPackage, DerivedPackageData>> latest = new ConcurrentDictionary<string, Tuple<IPackage, DerivedPackageData>>();

            // get settings
            bool checkFrameworks = EnableFrameworkFiltering;
            bool enableDelisting = EnableDelisting;
            // we need to save the current context because it's stored in TLS and we're computing hashes on different threads.
            var context = HttpContext.Current;

            // load and cache all packages.
            // Note that we can't pass GetPackageFiles() to Parallel.ForEach() because
            // the file could be added/deleted from _fileSystem, and if this happens,
            // we'll get error "Collection was modified; enumeration operation may not execute."
            // So we have to materialize the IEnumerable into a list first.
            var packageFiles = GetPackageFiles().ToList();

            Parallel.ForEach(packageFiles, opts, path =>
            {
                OptimizedZipPackage zip = OpenPackage(path);

                Debug.Assert(zip != null, "Unable to open " + path);
                if (zip == null)
                {
                    return;
                }
                if (enableDelisting)
                {
                    // hidden packages are considered delisted
                    zip.Listed = !File.GetAttributes(_fileSystem.GetFullPath(path)).HasFlag(FileAttributes.Hidden);
                }

                string packageHash = null;
                long packageSize = 0;
                string persistedHashFile = EnablePersistNupkgHash ? GetHashFile(path, false) : null;
                bool hashComputeNeeded = true;

                ReadHashFile(context, path, persistedHashFile, ref packageSize, ref packageHash, ref hashComputeNeeded);

                if (hashComputeNeeded)
                {
                    using (var stream = _fileSystem.OpenFile(path))
                    {
                        packageSize = stream.Length;
                        packageHash = Convert.ToBase64String(HashProvider.CalculateHash(stream));
                    }
                    WriteHashFile(context, path, persistedHashFile, packageSize, packageHash);
                }

                var data = new DerivedPackageData
                {
                    PackageSize = packageSize,
                    PackageHash = packageHash,
                    LastUpdated = _fileSystem.GetLastModified(path),
                    Created = _fileSystem.GetCreated(path),
                    Path = path,
                    FullPath = _fileSystem.GetFullPath(path),

                    // default to false, these will be set later
                    IsAbsoluteLatestVersion = false,
                    IsLatestVersion = false
                };

                if (checkFrameworks)
                {
                    data.SupportedFrameworks = zip.GetSupportedFrameworks();
                }

                var entry = new Tuple<IPackage, DerivedPackageData>(zip, data);

                // find the latest versions
                string id = zip.Id.ToLowerInvariant();

                // update with the highest version
                absoluteLatest.AddOrUpdate(id, entry, (oldId, oldEntry) => oldEntry.Item1.Version < entry.Item1.Version ? entry : oldEntry);

                // update latest for release versions
                if (zip.IsReleaseVersion())
                {
                    latest.AddOrUpdate(id, entry, (oldId, oldEntry) => oldEntry.Item1.Version < entry.Item1.Version ? entry : oldEntry);
                }

                // add the package to the cache, it should not exist already
                Debug.Assert(packages.ContainsKey(zip) == false, "duplicate package added");
                packages.AddOrUpdate(zip, entry.Item2, (oldPkg, oldData) => oldData);
            });

            // Set additional attributes after visiting all packages
            foreach (var entry in absoluteLatest.Values)
            {
                entry.Item2.IsAbsoluteLatestVersion = true;
            }

            foreach (var entry in latest.Values)
            {
                entry.Item2.IsLatestVersion = true;
            }

            return packages;
        }

        private void WriteHashFile(HttpContext context, string nupkgPath, string hashFilePath, long packageSize, string packageHash)
        {
            if (hashFilePath == null)
            {
                return; // feature not enabled.
            }
            try
            {
                var tempHashFilePath = GetHashFile(nupkgPath, true);
                _fileSystem.DeleteFile(tempHashFilePath);
                _fileSystem.DeleteFile(hashFilePath);

                var content = new StringBuilder();
                content.AppendLine(packageSize.ToString(CultureInfo.InvariantCulture));
                content.AppendLine(packageHash);

                using (var stream = new MemoryStream(Encoding.ASCII.GetBytes(content.ToString())))
                {
                    _fileSystem.AddFile(tempHashFilePath, stream);
                }
                // move temp file to official location when previous operation completed successfully to minimize impact of potential errors (ex: machine crash in the middle of saving the file).
                _fileSystem.MoveFile(tempHashFilePath, hashFilePath);
            }
            catch (Exception e)
            {
                // Hashing persistence is a perf optimization feature; we chose to degrade perf over degrading functionality in case of failure.
                Log(context, string.Format("Unable to create hash file '{0}'.", hashFilePath), e);
            }
        }

        private void ReadHashFile(HttpContext context, string nupkgPath, string hashFilePath, ref long packageSize, ref string packageHash, ref bool hashComputeNeeded)
        {
            if (hashFilePath == null)
            {
                return; // feature not enabled.
            }
            try
            {
                if (!_fileSystem.FileExists(hashFilePath) || _fileSystem.GetLastModified(hashFilePath) < _fileSystem.GetLastModified(nupkgPath))
                {
                    return; // hash does not exist or is not current.
                }
                using (var stream = _fileSystem.OpenFile(hashFilePath))
                {
                    var reader = new StreamReader(stream);
                    packageSize = long.Parse(reader.ReadLine(), CultureInfo.InvariantCulture);
                    packageHash = reader.ReadLine();
                }
                hashComputeNeeded = false;
            }
            catch (Exception e)
            {
                // Hashing persistence is a perf optimization feature; we chose to degrade perf over degrading functionality in case of failure.
                Log(context, string.Format("Unable to read hash file '{0}'.", hashFilePath), e);
            }
        }

        private static void Log(HttpContext context, string message, Exception innerException)
        {
            try
            {
                // Elmah.ErrorSignal.FromContext(context).Raise(new Exception(message, innerException));                
            }
            catch
            {
                // best effort
            }
        }

        private OptimizedZipPackage OpenPackage(string path)
        {
            OptimizedZipPackage zip = null;

            if (_fileSystem.FileExists(path))
            {
                try
                {
                    zip = new OptimizedZipPackage(_fileSystem, path);
                }
                catch (FileFormatException ex)
                {
                    throw new InvalidDataException(String.Format(CultureInfo.CurrentCulture, NuGetResources.ErrorReadingPackage, path), ex);
                }
                // Set the last modified date on the package
                zip.Published = _fileSystem.GetLastModified(path);
            }

            return zip;
        }

        // Add the file watcher to monitor changes on disk
        private void AttachEvents()
        {
            // skip invalid paths
            if (_fileWatcher == null && !String.IsNullOrEmpty(Source) && Directory.Exists(Source))
            {
                _fileWatcher = new FileSystemWatcher(Source);
                _fileWatcher.Filter = _filter;
                _fileWatcher.IncludeSubdirectories = false;

                _fileWatcher.Changed += FileChanged;
                _fileWatcher.Created += FileChanged;
                _fileWatcher.Deleted += FileChanged;
                _fileWatcher.Renamed += FileChanged;

                _fileWatcher.EnableRaisingEvents = true;
            }
        }

        // clean up events
        private void DetachEvents()
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Changed -= FileChanged;
                _fileWatcher.Created -= FileChanged;
                _fileWatcher.Deleted -= FileChanged;
                _fileWatcher.Renamed -= FileChanged;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
        }

        private void FileChanged(object sender, FileSystemEventArgs e)
        {
            // invalidate the cache when a nupkg in the root folder changes
            // TODO: invalidating *all* packages for every nupkg change under this folder seems more expensive than it should.
            // Recommend using e.FullPath to figure out which nupkgs need to be (re)computed.
            InvalidatePackages();
        }

        private bool AllowOverrideExistingPackageOnPush
        {
            get
            {
                // If the setting is misconfigured, treat it as success (backwards compatibility).
                return _getSetting("allowOverrideExistingPackageOnPush", true);
            }
        }

        private bool EnableDelisting
        {
            get
            {
                // If the setting is misconfigured, treat it as off (backwards compatibility).
                return _getSetting("enableDelisting", false);
            }
        }

        private bool EnableFrameworkFiltering
        {
            get
            {
                // If the setting is misconfigured, treat it as off (backwards compatibility).
                return _getSetting("enableFrameworkFiltering", false);
            }
        }

        private bool EnablePersistNupkgHash
        {
            get
            {
                // If the setting is misconfigured, treat it as off (backwards compatibility).
                return _getSetting("enablePersistNupkgHash", false);
            }
        }

        private static bool GetBooleanAppSetting(string key, bool defaultValue)
        {
            var appSettings = WebConfigurationManager.AppSettings;
            bool value;
            return !Boolean.TryParse(appSettings[key], out value) ? defaultValue : value;
        }
    }
}