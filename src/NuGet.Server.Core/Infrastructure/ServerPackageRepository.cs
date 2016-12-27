// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Server.Core.Logging;

namespace NuGet.Server.Core.Infrastructure
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
        private readonly ISettingsProvider _settingsProvider;

        private readonly IServerPackageStore _serverPackageStore;

        private readonly bool _runBackgroundTasks;
        private FileSystemWatcher _fileSystemWatcher;
        private bool _isFileSystemWatcherSuppressed = false;
        private bool _needsRebuild = true;

        private Timer _persistenceTimer;
        private Timer _rebuildTimer;

        public ServerPackageRepository(string path, IHashProvider hashProvider, ISettingsProvider settingsProvider = null, Logging.ILogger logger = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (hashProvider == null)
            {
                throw new ArgumentNullException(nameof(hashProvider));
            }

            _fileSystem = new PhysicalFileSystem(path);
            _runBackgroundTasks = true;
            _logger = logger ?? new TraceLogger();
            _expandedPackageRepository = new ExpandedPackageRepository(_fileSystem, hashProvider);
            _serverPackageStore = new ServerPackageStore(_fileSystem, Environment.MachineName.ToLowerInvariant() + ".cache.bin");
            _settingsProvider = settingsProvider ?? new DefaultSettingsProvider();
        }

        internal ServerPackageRepository(IFileSystem fileSystem, bool runBackgroundTasks, ExpandedPackageRepository innerRepository, ISettingsProvider settingsProvider = null, Logging.ILogger logger = null)
        {
            if (fileSystem == null)
            {
                throw new ArgumentNullException(nameof(fileSystem));
            }

            if (innerRepository == null)
            {
                throw new ArgumentNullException(nameof(innerRepository));
            }

            _fileSystem = fileSystem;
            _runBackgroundTasks = runBackgroundTasks;
            _expandedPackageRepository = innerRepository;
            _logger = logger ?? new TraceLogger();

            _serverPackageStore = new ServerPackageStore(_fileSystem, Environment.MachineName.ToLowerInvariant() + ".cache.bin");

            _settingsProvider = settingsProvider ?? new DefaultSettingsProvider();
        }

        private void SetupBackgroundJobs()
        {
            if (!_runBackgroundTasks)
            {
                return;
            }

            _logger.Log(LogLevel.Info, "Registering background jobs...");

            // Persist to package store at given interval (when dirty)
            _persistenceTimer = new Timer(state =>
                _serverPackageStore.PersistIfDirty(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

            // Rebuild the package store in the background (every hour)
            _rebuildTimer = new Timer(state =>
                RebuildPackageStore(), null, TimeSpan.FromSeconds(15), TimeSpan.FromHours(1));

            _logger.Log(LogLevel.Info, "Finished registering background jobs.");
        }

        public override IQueryable<IPackage> GetPackages()
        {
            return CachedPackages.AsQueryable();
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
            var cache = CachedPackages;

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

            using (LockAndSuppressFileSystemWatcher())
            {
                try
                {
                    var serverPackages = new HashSet<ServerPackage>(PackageEqualityComparer.IdAndVersion);

                    foreach (var packageFile in _fileSystem.GetFiles(_fileSystem.Root, "*.nupkg", false))
                    {
                        try
                        {
                            // Create package
                            var package = new OptimizedZipPackage(_fileSystem, packageFile);

                            // Is it a symbols package?
                            if (IgnoreSymbolsPackages && package.IsSymbolsPackage())
                            {
                                var message = string.Format(Strings.Error_SymbolsPackagesIgnored, package);

                                _logger.Log(LogLevel.Error, message);

                                continue;
                            }

                            // Allow overwriting package? If not, skip this one.
                            if (!AllowOverrideExistingPackageOnPush && _expandedPackageRepository.FindPackage(package.Id, package.Version) != null)
                            {
                                var message = string.Format(Strings.Error_PackageAlreadyExists, package);

                                _logger.Log(LogLevel.Error, message);

                                continue;
                            }

                            // Copy to correct filesystem location
                            _expandedPackageRepository.AddPackage(package);

                            // Mark for addition to metadata store
                            serverPackages.Add(CreateServerPackage(package, EnableDelisting));

                            // Remove file from drop folder
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

                    // Add packages to metadata store in bulk
                    _serverPackageStore.StoreRange(serverPackages);
                    _serverPackageStore.PersistIfDirty();

                    _logger.Log(LogLevel.Info, "Finished adding packages from drop folder.");
                }
                finally
                {
                    OptimizedZipPackage.PurgeCache();
                }
            }
        }

        /// <summary>
        /// Add a file to the repository.
        /// </summary>
        public override void AddPackage(IPackage package)
        {
            _logger.Log(LogLevel.Info, "Start adding package {0} {1}.", package.Id, package.Version);

            if (IgnoreSymbolsPackages && package.IsSymbolsPackage())
            {
                var message = string.Format(Strings.Error_SymbolsPackagesIgnored, package);

                _logger.Log(LogLevel.Error, message);
                throw new InvalidOperationException(message);
            }

            if (!AllowOverrideExistingPackageOnPush && FindPackage(package.Id, package.Version) != null)
            {
                var message = string.Format(Strings.Error_PackageAlreadyExists, package);

                _logger.Log(LogLevel.Error, message);
                throw new InvalidOperationException(message);
            }

            using (LockAndSuppressFileSystemWatcher())
            {
                // Copy to correct filesystem location
                _expandedPackageRepository.AddPackage(package);

                // Add to metadata store
                _serverPackageStore.Store(CreateServerPackage(package, EnableDelisting));

                _logger.Log(LogLevel.Info, "Finished adding package {0} {1}.", package.Id, package.Version);
            }
        }

        /// <summary>
        /// Unlist or delete a package.
        /// </summary>
        public override void RemovePackage(IPackage package)
        {
            if (package == null)
            {
                return;
            }

            using (LockAndSuppressFileSystemWatcher())
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
                            // Set "unlisted"
                            File.SetAttributes(fileName, File.GetAttributes(fileName) | FileAttributes.Hidden);

                            // Update metadata store
                            var serverPackage = FindPackage(package.Id, package.Version) as ServerPackage;
                            if (serverPackage != null)
                            {
                                serverPackage.Listed = false;
                                _serverPackageStore.Store(serverPackage);
                            }

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
                    // Remove from filesystem
                    _expandedPackageRepository.RemovePackage(package);

                    // Update metadata store
                    _serverPackageStore.Remove(package.Id, package.Version);

                    _logger.Log(LogLevel.Info, "Finished removing package {0} {1}.", package.Id, package.Version);
                }
            }
        }

        /// <summary>
        /// Remove a package from the repository.
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
            if (_persistenceTimer != null)
            {
                _persistenceTimer.Dispose();
            }

            if (_rebuildTimer != null)
            {
                _rebuildTimer.Dispose();
            }

            UnregisterFileSystemWatcher();
            _serverPackageStore.PersistIfDirty();
        }

        /// <summary>
        /// Internal package cache containing packages metadata. 
        /// This data is generated if it does not exist already.
        /// </summary>
        private IEnumerable<ServerPackage> CachedPackages
        {
            get
            {
                if (_needsRebuild || !_serverPackageStore.HasPackages())
                {
                    lock (_syncLock)
                    {
                        if (_needsRebuild || !_serverPackageStore.HasPackages())
                        {
                            RebuildPackageStore();
                        }
                    }
                }

                // First time we come here, attach the file system watcher
                if (_fileSystemWatcher == null)
                {
                    MonitorFileSystem(true);
                }

                // First time we come here, setup background jobs
                if (_persistenceTimer == null)
                {
                    SetupBackgroundJobs();
                }

                // Return packages
                return _serverPackageStore.GetAll();
            }
        }

        private void RebuildPackageStore()
        {
            lock (_syncLock)
            {
                _logger.Log(LogLevel.Info, "Start rebuilding package store...");

                // Build cache
                var packages = ReadPackagesFromDisk();
                _serverPackageStore.Clear();
                _serverPackageStore.StoreRange(packages);

                // Add packages from drop folder
                AddPackagesFromDropFolder();

                // Persist
                _serverPackageStore.PersistIfDirty();

                _needsRebuild = false;

                _logger.Log(LogLevel.Info, "Finished rebuilding package store.");
            }
        }

        /// <summary>
        /// ReadPackagesFromDisk loads all packages from disk and determines additional metadata such as the hash, IsAbsoluteLatestVersion, and IsLatestVersion.
        /// </summary>
        private HashSet<ServerPackage> ReadPackagesFromDisk()
        {
            _logger.Log(LogLevel.Info, "Start reading packages from disk...");

            using (LockAndSuppressFileSystemWatcher())
            {
                try
                {
                    var cachedPackages = new ConcurrentBag<ServerPackage>();

                    var enableDelisting = EnableDelisting;

                    var packages = _expandedPackageRepository.GetPackages().ToList();

                    Parallel.ForEach(packages, package =>
                    {
                        ServerPackage serverPackage;

                        // Try to create the server package and ignore a bad package if it fails
                        var couldCreateServerPackage = TryCreateServerPackage(package, enableDelisting, out serverPackage);
                        if (couldCreateServerPackage)
                        {
                            // Add the package to the cache, it should not exist already
                            if (cachedPackages.Contains(serverPackage))
                            {
                                _logger.Log(LogLevel.Warning, "Duplicate package found - {0} {1}", package.Id,
                                    package.Version);
                            }
                            else
                            {
                                cachedPackages.Add(serverPackage);
                            }
                        }
                    });

                    _logger.Log(LogLevel.Info, "Finished reading packages from disk.");
                    return new HashSet<ServerPackage>(cachedPackages, PackageEqualityComparer.IdAndVersion);
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, "Error while reading packages from disk: {0} {1}", ex.Message, ex.StackTrace);
                    throw;
                }
            }
        }

        private ServerPackage CreateServerPackage(IPackage package, bool enableDelisting)
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
            return serverPackage;
        }

        private bool TryCreateServerPackage(IPackage package, bool enableDelisting, out ServerPackage serverPackage)
        {
            try
            {
                serverPackage = CreateServerPackage(package, enableDelisting);
                return true;
            }
            catch(Exception e)
            {
                serverPackage = null;
                _logger.Log(LogLevel.Warning, "Unable to create server package - {0} {1}: {2}", package.Id, package.Version, e.Message);
                return false;
            }
        }
        
        /// <summary>
        /// Sets the current cache to null so it will be regenerated next time.
        /// </summary>
        public void ClearCache()
        {
            using (LockAndSuppressFileSystemWatcher())
            {
                OptimizedZipPackage.PurgeCache();

                _serverPackageStore.Clear();
                _serverPackageStore.Persist();
                _needsRebuild = true;
                _logger.Log(LogLevel.Info, "Cleared package cache.");
            }
        }

        private void MonitorFileSystem(bool monitor)
        {
            if (!EnableFileSystemMonitoring || !_runBackgroundTasks)
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
            if (EnableFileSystemMonitoring && _runBackgroundTasks && _fileSystemWatcher == null && !string.IsNullOrEmpty(Source) && Directory.Exists(Source))
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
            if (_isFileSystemWatcherSuppressed)
            {
                return;
            }

            _logger.Log(LogLevel.Verbose, "File system changed. File: {0} - Change: {1}", e.Name, e.ChangeType);

            // 1) If a .nupkg is dropped in the root, add it as a package
            if (String.Equals(Path.GetDirectoryName(e.FullPath), _fileSystemWatcher.Path, StringComparison.OrdinalIgnoreCase)
                && String.Equals(Path.GetExtension(e.Name), ".nupkg", StringComparison.OrdinalIgnoreCase))
            {
                // When a package is dropped into the server packages root folder, add it to the repository.
                AddPackagesFromDropFolder();
            }

            // 2) If a file is updated in a subdirectory, *or* a folder is deleted, invalidate the cache
            if ((!String.Equals(Path.GetDirectoryName(e.FullPath), _fileSystemWatcher.Path, StringComparison.OrdinalIgnoreCase) && File.Exists(e.FullPath))
                || e.ChangeType == WatcherChangeTypes.Deleted)
            {
                // TODO: invalidating *all* packages for every nupkg change under this folder seems more expensive than it should.
                // Recommend using e.FullPath to figure out which nupkgs need to be (re)computed.

                ClearCache();
            }
        }

        private bool AllowOverrideExistingPackageOnPush
        {
            get
            {
                // If the setting is misconfigured, treat it as success (backwards compatibility).
                return _settingsProvider.GetBoolSetting("allowOverrideExistingPackageOnPush", true);
            }
        }

        private bool IgnoreSymbolsPackages
        {
            get
            {
                // If the setting is misconfigured, treat it as "false" (backwards compatibility).
                return _settingsProvider.GetBoolSetting("ignoreSymbolsPackages", false);
            }
        }

        private bool EnableDelisting
        {
            get
            {
                // If the setting is misconfigured, treat it as off (backwards compatibility).
                return _settingsProvider.GetBoolSetting("enableDelisting", false);
            }
        }

        private bool EnableFrameworkFiltering
        {
            get
            {
                // If the setting is misconfigured, treat it as off (backwards compatibility).
                return _settingsProvider.GetBoolSetting("enableFrameworkFiltering", false);
            }
        }

        private bool EnableFileSystemMonitoring
        {
            get
            {
                // If the setting is misconfigured, treat it as on (backwards compatibility).
                return _settingsProvider.GetBoolSetting("enableFileSystemMonitoring", true);
            }
        }

        private string GetPackageFileName(string packageId, SemanticVersion version)
        {
            return string.Format(TemplateNupkgFilename, packageId, version.ToNormalizedString());
        }

        private string GetHashFileName(string packageId, SemanticVersion version)
        {
            return string.Format(TemplateHashFilename, packageId, version.ToNormalizedString(), NuGet.Constants.HashFileExtension);
        }
        
        private IDisposable LockAndSuppressFileSystemWatcher()
        {
            return new SupressedFileSystemWatcher(this);
        }

        private class SupressedFileSystemWatcher : IDisposable
        {
            private readonly ServerPackageRepository _repository;

            public SupressedFileSystemWatcher(ServerPackageRepository repository)
            {
                if (repository == null)
                {
                    throw new ArgumentNullException(nameof(repository));
                }

                _repository = repository;

                // Lock the repository.
                bool lockTaken = false;
                try
                {
                    Monitor.Enter(_repository._syncLock, ref lockTaken);
                }
                catch
                {
                    if (lockTaken)
                    {
                        Monitor.Exit(_repository._syncLock);
                    }

                    throw;
                }

                // Suppress the file system events.
                _repository._isFileSystemWatcherSuppressed = true;
            }

            public void Dispose()
            {
                Monitor.Exit(_repository._syncLock);

                _repository._isFileSystemWatcherSuppressed = false;
            }
        }
    }
}