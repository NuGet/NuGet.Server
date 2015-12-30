// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Web.Configuration;
using NuGet.Resources;
using NuGet.Server.Logging;

namespace NuGet.Server.Infrastructure
{
    /// <summary>
    /// ServerPackageRepository represents a folder of nupkgs on disk. All packages are cached during the first request in order
    /// to correctly determine attributes such as IsAbsoluteLatestVersion. Adding, removing, or making changes to packages on disk 
    /// will clear the cache.
    /// </summary>
    public class ServerPackageRepository
        : PackageRepositoryBase, IServerPackageRepository, IPackageLookup, IDisposable
    {
        private const string TemplateNupkgFilename = "{0}\\{1}\\{0}.{1}.nupkg";
        private const string TemplateHashFilename = "{0}\\{1}\\{0}.{1}{2}";

        private readonly object _syncLock = new object();

        private readonly IFileSystem _fileSystem;
        private readonly ExpandedPackageRepository _expandedPackageRepository;
        private readonly Logging.ILogger _logger;
        private readonly Func<string, bool, bool> _getSetting;

        private HashSet<ServerPackage> _packages;

        private readonly bool _monitorFileSystem;
        private FileSystemWatcher _fileSystemWatcher;
        
        public ServerPackageRepository(string path, IHashProvider hashProvider, Logging.ILogger logger)
            : this(new PhysicalFileSystem(path), true, hashProvider, logger)
        {
        }

        internal ServerPackageRepository(IFileSystem fileSystem, bool monitorFileSystem, IHashProvider hashProvider, Logging.ILogger logger = null, Func<string, bool, bool> getSetting = null)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException("fileSystem");
            }

            if (hashProvider == null)
            {
                throw new ArgumentNullException("hashProvider");
            }

            _fileSystem = fileSystem;
            _monitorFileSystem = monitorFileSystem;
            _logger = logger ?? new TraceLogger();
            _expandedPackageRepository = new ExpandedPackageRepository(fileSystem, hashProvider);

            _getSetting = getSetting ?? GetBooleanAppSetting;
        }

        internal ServerPackageRepository(IFileSystem fileSystem, bool monitorFileSystem, ExpandedPackageRepository innerRepository, Logging.ILogger logger = null, Func<string, bool, bool> getSetting = null)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException("fileSystem");
            }

            if (innerRepository == null)
            {
                throw new ArgumentNullException("innerRepository");
            }

            _fileSystem = fileSystem;
            _monitorFileSystem = monitorFileSystem;
            _expandedPackageRepository = innerRepository;
            _logger = logger ?? new TraceLogger();

            _getSetting = getSetting ?? GetBooleanAppSetting;
        }
        
        public override IQueryable<IPackage> GetPackages()
        {
            return PackageCache.AsQueryable();
        }

        public bool Exists(string packageId, SemanticVersion version)
        {
            return FindPackage(packageId, version) != null;
        }

        public IPackage FindPackage(string packageId, SemanticVersion version)
        {
            return FindPackagesById(packageId)
                .FirstOrDefault(p => p.Version.Equals(version));
        }

        public IEnumerable<IPackage> FindPackagesById(string packageId)
        {
            return GetPackages()
                .Where(p => StringComparer.OrdinalIgnoreCase.Compare(p.Id, packageId) == 0);
        }
        
        public IQueryable<IPackage> Search(string searchTerm, IEnumerable<string> targetFrameworks, bool allowPrereleaseVersions)
        {
            var cache = PackageCache;

            var packages = cache.AsQueryable()
                .Find(searchTerm)
                .FilterByPrerelease(allowPrereleaseVersions);

            if (EnableDelisting)
            {
                packages = packages.Where(p => p.Listed);
            }

            if (EnableFrameworkFiltering && targetFrameworks.Any())
            {
                // Get the list of framework names
                var frameworkNames = targetFrameworks.Select(frameworkName => VersionUtility.ParseFrameworkName(frameworkName));

                packages = packages.Where(package => frameworkNames.Any(frameworkName => VersionUtility.IsCompatible(frameworkName, package.GetSupportedFrameworks())));
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
                return _expandedPackageRepository.Source;
            }
        }

        public override bool SupportsPrereleasePackages
        {
            get
            {
                return _expandedPackageRepository.SupportsPrereleasePackages;
            }
        }

        private void AddPackagesFromDropFolder()
        {
            _logger.Log(LogLevel.Info, "Start adding packages from drop folder.");

            MonitorFileSystem(false);

            try
            {
                foreach (var packageFile in _fileSystem.GetFiles(_fileSystem.Root, "*.nupkg", false))
                {
                    try
                    {
                        var package = new ZipPackage(_fileSystem.OpenFile(packageFile));

                        _expandedPackageRepository.AddPackage(package);

                        _fileSystem.DeleteFile(packageFile);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        // The file may be in use (still being copied) - ignore the error
                        _logger.Log(LogLevel.Error, "Error adding package file {0} from drop folder: {1}", packageFile, ex.Message);
                    }
                    catch (IOException ex)
                    {
                        // The file may be in use (still being copied) - ignore the error
                        _logger.Log(LogLevel.Error, "Error adding package file {0} from drop folder: {1}", packageFile, ex.Message);
                    }
                }

                _logger.Log(LogLevel.Info, "Finished adding packages from drop folder.");
            }
            finally
            {
                MonitorFileSystem(true);
            }
        }

        /// <summary>
        /// Add a file to the repository.
        /// </summary>
        public override void AddPackage(IPackage package)
        {
            _logger.Log(LogLevel.Info, "Start adding package {0} {1}.", package.Id, package.Version);

            if (!AllowOverrideExistingPackageOnPush && FindPackage(package.Id, package.Version) != null)
            {
                var message = string.Format(NuGetResources.Error_PackageAlreadyExists, package);

                _logger.Log(LogLevel.Error, message);
                throw new InvalidOperationException(message);
            }

            lock (_syncLock)
            {
                MonitorFileSystem(false);
                try
                {
                    _expandedPackageRepository.AddPackage(package);
                    _logger.Log(LogLevel.Info, "Finished adding package {0} {1}.", package.Id, package.Version);

                    ClearCache();
                }
                finally
                {
                    MonitorFileSystem(true);
                }
            }
        }

        /// <summary>
        /// Unlist or delete a package.
        /// </summary>
        public override void RemovePackage(IPackage package)
        {
            if (package != null)
            {
                MonitorFileSystem(false);
                try
                {
                    lock (_syncLock)
                    {
                        _logger.Log(LogLevel.Info, "Start removing package {0} {1}.", package.Id, package.Version);

                        if (EnableDelisting)
                        {
                            var physicalFileSystem = _fileSystem as PhysicalFileSystem;
                            if (physicalFileSystem != null)
                            {
                                var fileName = physicalFileSystem.GetFullPath(
                                    GetPackageFileName(package.Id, package.Version));

                                if (File.Exists(fileName))
                                {
                                    File.SetAttributes(fileName, File.GetAttributes(fileName) | FileAttributes.Hidden);

                                    // Note that delisted files can still be queried, therefore not deleting persisted hashes if present.
                                    // Also, no need to flip hidden attribute on these since only the one from the nupkg is queried.

                                    _logger.Log(LogLevel.Info, "Unlisted package {0} {1}.", package.Id, package.Version);
                                }
                                else
                                {
                                    _logger.Log(LogLevel.Error,
                                        "Error removing package {0} {1} - could not find package file {2}", 
                                            package.Id, package.Version, fileName);
                                }
                            }
                        }
                        else
                        {
                            _expandedPackageRepository.RemovePackage(package);

                            _logger.Log(LogLevel.Info, "Finished removing package {0} {1}.", package.Id, package.Version);
                        }

                        ClearCache();
                    }
                }
                finally
                {
                    MonitorFileSystem(true);
                }
            }
        }

        /// <summary>
        /// Remove a package from the respository.
        /// </summary>
        public void RemovePackage(string packageId, SemanticVersion version)
        {
            var package = FindPackage(packageId, version);

            RemovePackage(package);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            UnregisterFileSystemWatcher();
        }
        
        /// <summary>
        /// Internal package cache containing packages metadata. 
        /// This data is generated if it does not exist already.
        /// </summary>
        private HashSet<ServerPackage> PackageCache
        {
            get
            {
                if (_packages == null)
                {
                    lock (_syncLock)
                    {
                        if (_packages == null)
                        {
                            if (_fileSystemWatcher == null)
                            {
                                // first time we come here, attach the file system watcher 
                                MonitorFileSystem(true);
                            }

                            // add packages from drop folder
                            AddPackagesFromDropFolder();

                            // build cache
                            _packages = BuildCache();
                        }
                    }
                }

                return _packages;
            }
        }

        /// <summary>
        /// BuildCache loads all packages and determines additional metadata such as the hash, IsAbsoluteLatestVersion, and IsLatestVersion.
        /// </summary>
        private HashSet<ServerPackage> BuildCache()
        {
            _logger.Log(LogLevel.Info, "Start building package cache.");
            MonitorFileSystem(false);
            
            try
            {
                var cachedPackages = new ConcurrentBag<ServerPackage>();

                var opts = new ParallelOptions { MaxDegreeOfParallelism = 4 };

                var absoluteLatest = new ConcurrentDictionary<string, ServerPackage>();
                var latest = new ConcurrentDictionary<string, ServerPackage>();

                bool enableDelisting = EnableDelisting;

                var packages = _expandedPackageRepository.GetPackages().ToList();

                Parallel.ForEach(packages, opts, package =>
                {
                    // File names
                    var packageFileName = GetPackageFileName(package.Id, package.Version);
                    var hashFileName = GetHashFileName(package.Id, package.Version);

                    // File system
                    var physicalFileSystem = _fileSystem as PhysicalFileSystem;

                    // Build package info
                    var packageDerivedData = new PackageDerivedData();

                    // Read package hash
                    using (var reader = new StreamReader(_fileSystem.OpenFile(hashFileName)))
                    {
                        packageDerivedData.PackageHash = reader.ReadToEnd().Trim();
                    }

                    // Read package info
                    var localPackage = package as LocalPackage;
                    if (physicalFileSystem != null)
                    {
                        // Read package info from file system
                        var fileInfo = new FileInfo(_fileSystem.GetFullPath(packageFileName));
                        packageDerivedData.PackageSize = fileInfo.Length;

                        packageDerivedData.LastUpdated = _fileSystem.GetLastModified(packageFileName);
                        packageDerivedData.Created = _fileSystem.GetCreated(packageFileName);
                        packageDerivedData.Path = packageFileName;
                        packageDerivedData.FullPath = _fileSystem.GetFullPath(packageFileName);

                        if (enableDelisting && localPackage != null)
                        {
                            // hidden packages are considered delisted
                            localPackage.Listed = !fileInfo.Attributes.HasFlag(FileAttributes.Hidden);
                        }
                    }
                    else
                    {
                        // Read package info from package (slower)
                        using (var stream = package.GetStream())
                        {
                            packageDerivedData.PackageSize = stream.Length;
                        }

                        packageDerivedData.LastUpdated = DateTime.MinValue;
                        packageDerivedData.Created = DateTime.MinValue;
                    }

                    // TODO: frameworks?

                    // Build entry
                    var serverPackage = new ServerPackage(package, packageDerivedData);
                    serverPackage.IsAbsoluteLatestVersion = false;
                    serverPackage.IsLatestVersion = false;

                    // Find the latest versions
                    string id = package.Id.ToLowerInvariant();

                    // Update with the highest version
                    absoluteLatest.AddOrUpdate(id, serverPackage,
                        (oldId, oldEntry) => oldEntry.Version < serverPackage.Version ? serverPackage : oldEntry);

                    // Update latest for release versions
                    if (package.IsReleaseVersion())
                    {
                        latest.AddOrUpdate(id, serverPackage,
                            (oldId, oldEntry) => oldEntry.Version < serverPackage.Version ? serverPackage : oldEntry);
                    }

                    // Add the package to the cache, it should not exist already
                    if (cachedPackages.Contains(serverPackage))
                    {
                        _logger.Log(LogLevel.Warning, "Duplicate package found - {0} {1}", package.Id, package.Version);
                    }
                    else
                    {
                        cachedPackages.Add(serverPackage);
                    }
                });

                // Set additional attributes after visiting all packages
                foreach (var entry in absoluteLatest.Values)
                {
                    entry.IsAbsoluteLatestVersion = true;
                }

                foreach (var entry in latest.Values)
                {
                    entry.IsLatestVersion = true;
                }

                _logger.Log(LogLevel.Info, "Finished building package cache.");
                return new HashSet<ServerPackage>(cachedPackages, PackageEqualityComparer.IdAndVersion);
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, "Error while building package cache: {0} {1}", ex.Message, ex.StackTrace);
                throw;
            }
            finally
            {
                MonitorFileSystem(true);
            }
        }

        /// <summary>
        /// Sets the current cache to null so it will be regenerated next time.
        /// </summary>
        public void ClearCache()
        {
            lock (_syncLock)
            {
                _packages = null;
                _logger.Log(LogLevel.Info, "Cleared package cache.");
            }
        }

        private void MonitorFileSystem(bool monitor)
        {
            if (!_monitorFileSystem)
            {
                return;
            }

            if (_fileSystemWatcher != null)
            {
                _fileSystemWatcher.EnableRaisingEvents = monitor;
            }
            else
            {
                if (monitor)
                {
                    RegisterFileSystemWatcher();
                }
                else
                {
                    UnregisterFileSystemWatcher();
                }
            }

            _logger.Log(LogLevel.Verbose, "Monitoring {0} for new packages: {1}", Source, monitor);
        }

        /// <summary>
        /// Registers the file system watcher to monitor changes on disk.
        /// </summary>
        private void RegisterFileSystemWatcher()
        {
            // When files are moved around, recreate the package cache
            if (_monitorFileSystem && _fileSystemWatcher == null && !string.IsNullOrEmpty(Source) && Directory.Exists(Source))
            {
                // ReSharper disable once UseObjectOrCollectionInitializer
                _fileSystemWatcher = new FileSystemWatcher(Source);
                _fileSystemWatcher.Filter = "*";
                _fileSystemWatcher.IncludeSubdirectories = true;
                
                _fileSystemWatcher.Changed += FileSystemChanged;
                _fileSystemWatcher.Created += FileSystemChanged;
                _fileSystemWatcher.Deleted += FileSystemChanged;
                _fileSystemWatcher.Renamed += FileSystemChanged;

                _fileSystemWatcher.EnableRaisingEvents = true;

                _logger.Log(LogLevel.Verbose, "Created FileSystemWatcher - monitoring {0}.", Source);
            }
        }

        /// <summary>
        /// Unregisters and clears events of the file system watcher to monitor changes on disk.
        /// </summary>
        private void UnregisterFileSystemWatcher()
        {
            if (_fileSystemWatcher != null)
            {
                _fileSystemWatcher.EnableRaisingEvents = false;
                _fileSystemWatcher.Changed -= FileSystemChanged;
                _fileSystemWatcher.Created -= FileSystemChanged;
                _fileSystemWatcher.Deleted -= FileSystemChanged;
                _fileSystemWatcher.Renamed -= FileSystemChanged;
                _fileSystemWatcher.Dispose();
                _fileSystemWatcher = null;

                _logger.Log(LogLevel.Verbose, "Destroyed FileSystemWatcher - no longer monitoring {0}.", Source);
            }
        }
        
        private void FileSystemChanged(object sender, FileSystemEventArgs e)
        {
            _logger.Log(LogLevel.Verbose, "File system changed. File: {0} - Change: {1}", e.Name, e.ChangeType);

            if (Path.GetDirectoryName(e.FullPath) == _fileSystemWatcher.Path)
            {
                // When a package is dropped into the server packages root folder, add it to the repository.
                AddPackagesFromDropFolder();
            }
            
            // Invalidate the cache when a nupkg in the packages folder changes
            // TODO: invalidating *all* packages for every nupkg change under this folder seems more expensive than it should.
            // Recommend using e.FullPath to figure out which nupkgs need to be (re)computed.
            ClearCache();
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
        
        private static bool GetBooleanAppSetting(string key, bool defaultValue)
        {
            var appSettings = WebConfigurationManager.AppSettings;
            bool value;
            return !Boolean.TryParse(appSettings[key], out value) ? defaultValue : value;
        }

        private string GetPackageFileName(string packageId, SemanticVersion version)
        {
            return string.Format(TemplateNupkgFilename, packageId, version.ToNormalizedString());
        }

        private string GetHashFileName(string packageId, SemanticVersion version)
        {
            return string.Format(TemplateHashFilename, packageId, version.ToNormalizedString(), NuGet.Constants.HashFileExtension);
        }
    }
}