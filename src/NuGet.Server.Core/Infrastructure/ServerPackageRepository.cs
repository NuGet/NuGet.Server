// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NuGet.Server.Core.Logging;

namespace NuGet.Server.Core.Infrastructure
{
    /// <summary>
    /// ServerPackageRepository represents a folder of nupkgs on disk. All packages are cached during the first request
    /// in order to correctly determine attributes such as IsAbsoluteLatestVersion. Adding, removing, or making changes
    /// to packages on disk will clear the cache. This implementation is the core business logic for dealing with
    /// packages on the server side and deals with the the underlying concerns of storing packages both on disk
    /// and in memory (<see cref="IServerPackageStore"/> and <see cref="IServerPackageCache"/>, respectively).
    /// </summary>
    public class ServerPackageRepository
        : IServerPackageRepository, IDisposable
    {
        private readonly object _syncLock = new object();

        private readonly IFileSystem _fileSystem;
        private readonly IServerPackageStore _serverPackageStore;
        private readonly Logging.ILogger _logger;
        private readonly ISettingsProvider _settingsProvider;

        private readonly IServerPackageCache _serverPackageCache;

        private readonly bool _runBackgroundTasks;
        private FileSystemWatcher _fileSystemWatcher;
        private bool _isFileSystemWatcherSuppressed;
        private bool _needsRebuild;

        private Timer _persistenceTimer;
        private Timer _rebuildTimer;

        public ServerPackageRepository(
            string path,
            IHashProvider hashProvider,
            ISettingsProvider settingsProvider = null,
            Logging.ILogger logger = null)
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
            _settingsProvider = settingsProvider ?? new DefaultSettingsProvider();
            _logger = logger ?? new TraceLogger();
            _serverPackageCache = InitializeServerPackageStore();
            _serverPackageStore = new ServerPackageStore(
                _fileSystem,
                new ExpandedPackageRepository(_fileSystem, hashProvider),
                _logger);
        }

        internal ServerPackageRepository(
            IFileSystem fileSystem,
            bool runBackgroundTasks,
            ExpandedPackageRepository innerRepository,
            ISettingsProvider settingsProvider = null,
            Logging.ILogger logger = null)
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
            _settingsProvider = settingsProvider ?? new DefaultSettingsProvider();
            _logger = logger ?? new TraceLogger();
            _serverPackageCache = InitializeServerPackageStore();
            _serverPackageStore = new ServerPackageStore(
                _fileSystem,
                innerRepository,
                _logger);
        }

        public string Source => _fileSystem.Root;

        private bool AllowOverrideExistingPackageOnPush =>
            _settingsProvider.GetBoolSetting("allowOverrideExistingPackageOnPush", true);

        private bool IgnoreSymbolsPackages =>
            _settingsProvider.GetBoolSetting("ignoreSymbolsPackages", false);

        private bool EnableDelisting =>
            _settingsProvider.GetBoolSetting("enableDelisting", false);

        private bool EnableFrameworkFiltering =>
            _settingsProvider.GetBoolSetting("enableFrameworkFiltering", false);

        private bool EnableFileSystemMonitoring =>
            _settingsProvider.GetBoolSetting("enableFileSystemMonitoring", true);

        private ServerPackageCache InitializeServerPackageStore()
        {
            return new ServerPackageCache(_fileSystem, Environment.MachineName.ToLowerInvariant() + ".cache.bin");
        }

        /// <summary>
        /// Package cache containing packages metadata. 
        /// This data is generated if it does not exist already.
        /// </summary>
        public IQueryable<IServerPackage> GetPackages()
        {
		    /*
             * We rebuild the package storage under either of two conditions:
             *
             * 1. If the "needs rebuild" flag is set to true. This is initially the case when the repository is
             *    instantiated, if a non-package drop file system event occurred (e.g. a file deletion), or if the
             *    cache was manually cleared.
             *
             * 2. If the store has no packages at all. This is so we pick up initial packages as quickly as
             *    possible.
             */
            if (_needsRebuild || _serverPackageCache.IsEmpty())
            {
                lock (_syncLock)
                {
                    if (_needsRebuild || _serverPackageCache.IsEmpty())
                    {
                        RebuildPackageStore();
                    }
                }
            }

            // First time we come here, attach the file system watcher.
            if (_fileSystemWatcher == null &&
                EnableFileSystemMonitoring &&
                _runBackgroundTasks)
            {
                RegisterFileSystemWatcher();
            }

            // First time we come here, setup background jobs.
            if (_persistenceTimer == null &&
                _runBackgroundTasks)
            {
                SetupBackgroundJobs();
            }

            // Return packages
            return _serverPackageCache
                .GetAll()
                .AsQueryable();
        }

        public IQueryable<IServerPackage> Search(string searchTerm, IEnumerable<string> targetFrameworks, bool allowPrereleaseVersions)
        {
            var cache = GetPackages();

            var packages = cache
                .Find(searchTerm)
                .FilterByPrerelease(allowPrereleaseVersions);

            if (EnableDelisting)
            {
                packages = packages.Where(p => p.Listed);
            }

            if (EnableFrameworkFiltering && targetFrameworks.Any())
            {
                // Get the list of framework names
                var frameworkNames = targetFrameworks
                    .Select(frameworkName => VersionUtility.ParseFrameworkName(frameworkName));

                packages = packages
                    .Where(package => frameworkNames
                        .Any(frameworkName => VersionUtility
                            .IsCompatible(frameworkName, package.GetSupportedFrameworks())));
            }

            return packages;
        }

        private void AddPackagesFromDropFolder()
        {
            _logger.Log(LogLevel.Info, "Start adding packages from drop folder.");

            using (LockAndSuppressFileSystemWatcher())
            {
                try
                {
                    var serverPackages = new HashSet<ServerPackage>(IdAndVersionEqualityComparer.Instance);

                    foreach (var packageFile in _fileSystem.GetFiles(_fileSystem.Root, "*.nupkg", false))
                    {
                        try
                        {
                            // Create package
                            var package = new OptimizedZipPackage(_fileSystem, packageFile);

                            if (!CanPackageBeAdded(package, shouldThrow: false))
                            {
                                continue;
                            }

                            // Add the package to the file system store.
                            var serverPackage = _serverPackageStore.Add(
                                package,
                                EnableDelisting);

                            // Keep track of the the package for addition to metadata store.
                            serverPackages.Add(serverPackage);

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
                    _serverPackageCache.AddRange(serverPackages);
                    _serverPackageCache.PersistIfDirty();

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
        public void AddPackage(IPackage package)
        {
            _logger.Log(LogLevel.Info, "Start adding package {0} {1}.", package.Id, package.Version);

            CanPackageBeAdded(package, shouldThrow: true);

            using (LockAndSuppressFileSystemWatcher())
            {
                // Add the package to the file system store.
                var serverPackage = _serverPackageStore.Add(
                    package,
                    EnableDelisting);

                // Add the package to the metadata store.
                _serverPackageCache.Add(serverPackage);

                _logger.Log(LogLevel.Info, "Finished adding package {0} {1}.", package.Id, package.Version);
            }
        }

        private bool CanPackageBeAdded(IPackage package, bool shouldThrow)
        {
            if (IgnoreSymbolsPackages && package.IsSymbolsPackage())
            {
                var message = string.Format(Strings.Error_SymbolsPackagesIgnored, package);

                _logger.Log(LogLevel.Error, message);

                if (shouldThrow)
                {
                    throw new InvalidOperationException(message);
                }

                return false;
            }

            // Does the package already exist?
            if (!AllowOverrideExistingPackageOnPush && this.FindPackage(package.Id, package.Version) != null)
            {
                var message = string.Format(Strings.Error_PackageAlreadyExists, package);

                _logger.Log(LogLevel.Error, message);

                if (shouldThrow)
                {
                    throw new InvalidOperationException(message);
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Remove a package from the repository.
        /// </summary>
        public void RemovePackage(string id, SemanticVersion version)
        {
            _logger.Log(LogLevel.Info, "Start removing package {0} {1}.", id, version);

            var package = this.FindPackage(id, version);

            if (package == null)
            {
                _logger.Log(LogLevel.Info, "No-op when removing package {0} {1} because it doesn't exist.", id, version);
                return;
            }

            using (LockAndSuppressFileSystemWatcher())
            {
                // Update the file system.
                _serverPackageStore.Remove(package.Id, package.Version, EnableDelisting);

                // Update the metadata store.
                _serverPackageCache.Remove(package.Id, package.Version, EnableDelisting);

                if (EnableDelisting)
                {
                    _logger.Log(LogLevel.Info, "Unlisted package {0} {1}.", package.Id, package.Version);
                }
                else
                {

                    _logger.Log(LogLevel.Info, "Finished removing package {0} {1}.", package.Id, package.Version);
                }
            }
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
            _serverPackageCache.PersistIfDirty();
        }

        private void RebuildPackageStore()
        {
            lock (_syncLock)
            {
                _logger.Log(LogLevel.Info, "Start rebuilding package store...");

                // Build cache
                var packages = ReadPackagesFromDisk();
                _serverPackageCache.Clear();
                _serverPackageCache.AddRange(packages);

                // Add packages from drop folder
                AddPackagesFromDropFolder();

                // Persist
                _serverPackageCache.PersistIfDirty();

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
                    var packages = _serverPackageStore.GetAll(EnableDelisting);

                    _logger.Log(LogLevel.Info, "Finished reading packages from disk.");
;
                    return packages;
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, "Error while reading packages from disk: {0} {1}", ex.Message, ex.StackTrace);

                    throw;
                }
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

                _serverPackageCache.Clear();
                _serverPackageCache.Persist();
                _needsRebuild = true;
                _logger.Log(LogLevel.Info, "Cleared package cache.");
            }
        }

        private void SetupBackgroundJobs()
        {
            _logger.Log(LogLevel.Info, "Registering background jobs...");

            // Persist to package store at given interval (when dirty)
            _persistenceTimer = new Timer(state =>
                _serverPackageCache.PersistIfDirty(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

            // Rebuild the package store in the background (every hour)
            _rebuildTimer = new Timer(state =>
                RebuildPackageStore(), null, TimeSpan.FromSeconds(15), TimeSpan.FromHours(1));

            _logger.Log(LogLevel.Info, "Finished registering background jobs.");
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
            if (string.Equals(Path.GetDirectoryName(e.FullPath), _fileSystemWatcher.Path, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetExtension(e.Name), ".nupkg", StringComparison.OrdinalIgnoreCase))
            {
                // When a package is dropped into the server packages root folder, add it to the repository.
                AddPackagesFromDropFolder();
            }

            // 2) If a file is updated in a subdirectory, *or* a folder is deleted, invalidate the cache
            if ((!string.Equals(Path.GetDirectoryName(e.FullPath), _fileSystemWatcher.Path, StringComparison.OrdinalIgnoreCase) && File.Exists(e.FullPath))
                || e.ChangeType == WatcherChangeTypes.Deleted)
            {
                // TODO: invalidating *all* packages for every nupkg change under this folder seems more expensive than it should.
                // Recommend using e.FullPath to figure out which nupkgs need to be (re)computed.

                ClearCache();
            }
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