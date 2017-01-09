// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly SemaphoreSlim _syncLock = new SemaphoreSlim(1);

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
        public async Task<IEnumerable<IServerPackage>> GetPackagesAsync(CancellationToken token)
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
                using (await LockAndSuppressFileSystemWatcherAsync(token))
                {
                    if (_needsRebuild || _serverPackageCache.IsEmpty())
                    {
                        await RebuildPackageStoreWithoutLockingAsync(token);
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
            return _serverPackageCache.GetAll();
        }

        public async Task<IEnumerable<IServerPackage>> SearchAsync(
            string searchTerm,
            IEnumerable<string> targetFrameworks,
            bool allowPrereleaseVersions,
            CancellationToken token)
        {
            var cache = await GetPackagesAsync(token);

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

        private async Task AddPackagesFromDropFolderAsync(CancellationToken token)
        {
            using (await LockAndSuppressFileSystemWatcherAsync(token))
            {
                await AddPackagesFromDropFolderWithoutLockingAsync(token);
            }
        }

        /// <summary>
        /// This method requires <see cref="LockAndSuppressFileSystemWatcherAsync(CancellationToken)"/>.
        /// </summary>
        private async Task AddPackagesFromDropFolderWithoutLockingAsync(CancellationToken token)
        {
            _logger.Log(LogLevel.Info, "Start adding packages from drop folder.");

            try
            {
                var serverPackages = new HashSet<ServerPackage>(IdAndVersionEqualityComparer.Instance);

                foreach (var packageFile in _fileSystem.GetFiles(_fileSystem.Root, "*.nupkg", recursive: false))
                {
                    try
                    {
                        // Create package
                        var package = new OptimizedZipPackage(_fileSystem, packageFile);

                        if (!await CanPackageBeAddedAsync(package, shouldThrow: false, token: token))
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

        /// <summary>
        /// Add a file to the repository.
        /// </summary>
        public async Task AddPackageAsync(IPackage package, CancellationToken token)
        {
            _logger.Log(LogLevel.Info, "Start adding package {0} {1}.", package.Id, package.Version);

            await CanPackageBeAddedAsync(package, shouldThrow: true, token: token);

            using (await LockAndSuppressFileSystemWatcherAsync(token))
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

        private async Task<bool> CanPackageBeAddedAsync(IPackage package, bool shouldThrow, CancellationToken token)
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
            if (!AllowOverrideExistingPackageOnPush && await this.FindPackageAsync(package.Id, package.Version, token) != null)
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
        public async Task RemovePackageAsync(string id, SemanticVersion version, CancellationToken token)
        {
            _logger.Log(LogLevel.Info, "Start removing package {0} {1}.", id, version);

            var package = await this.FindPackageAsync(id, version, token);

            if (package == null)
            {
                _logger.Log(LogLevel.Info, "No-op when removing package {0} {1} because it doesn't exist.", id, version);
                return;
            }

            using (await LockAndSuppressFileSystemWatcherAsync(token))
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

        /// <summary>
        /// This is an event handler for background work. Therefore, it should never throw exceptions.
        /// </summary>
        private async void RebuildPackageStoreAsync(CancellationToken token)
        {
            try
            {
                using (await LockAndSuppressFileSystemWatcherAsync(token))
                {
                    await RebuildPackageStoreWithoutLockingAsync(token);
                }
            }
            catch (Exception exception)
            {
                _logger.Log(LogLevel.Error, "An exception occurred while rebuilding the package store: {0}", exception);
            }
        }

        /// <summary>
        /// This method requires <see cref="LockAndSuppressFileSystemWatcherAsync(CancellationToken)"/>.
        /// </summary>
        private async Task RebuildPackageStoreWithoutLockingAsync(CancellationToken token)
        {
            _logger.Log(LogLevel.Info, "Start rebuilding package store...");

            // Build cache
            var packages = await ReadPackagesFromDiskWithoutLockingAsync(token);
            _serverPackageCache.Clear();
            _serverPackageCache.AddRange(packages);

            // Add packages from drop folder
            await AddPackagesFromDropFolderWithoutLockingAsync(token);

            // Persist
            _serverPackageCache.PersistIfDirty();

            _needsRebuild = false;

            _logger.Log(LogLevel.Info, "Finished rebuilding package store.");
        }

        /// <summary>
        /// Loads all packages from disk and determines additional metadata such as the hash,
        /// IsAbsoluteLatestVersion, and IsLatestVersion.
        /// 
        /// This method requires <see cref="LockAndSuppressFileSystemWatcherAsync(CancellationToken)"/>.
        /// </summary>
        private async Task<HashSet<ServerPackage>> ReadPackagesFromDiskWithoutLockingAsync(CancellationToken token)
        {
            _logger.Log(LogLevel.Info, "Start reading packages from disk...");

            try
            {
                var packages = await _serverPackageStore.GetAllAsync(EnableDelisting, token);

                _logger.Log(LogLevel.Info, "Finished reading packages from disk.");

                return packages;
            }
            catch (Exception ex)
            {
                _logger.Log(
                    LogLevel.Error,
                    "Error while reading packages from disk: {0} {1}",
                    ex.Message,
                    ex.StackTrace);

                throw;
            }
        }
        
        /// <summary>
        /// Sets the current cache to null so it will be regenerated next time.
        /// </summary>
        public async Task ClearCacheAsync(CancellationToken token)
        {
            using (await LockAndSuppressFileSystemWatcherAsync(token))
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
            _persistenceTimer = new Timer(
                callback: state => _serverPackageCache.PersistIfDirty(),
                state: null,
                dueTime: TimeSpan.FromMinutes(1),
                period: TimeSpan.FromMinutes(1));

            // Rebuild the package store in the background (every hour)
            _rebuildTimer = new Timer(
                callback: state => RebuildPackageStoreAsync(CancellationToken.None),
                state: null,
                dueTime: TimeSpan.FromSeconds(15),
                period: TimeSpan.FromHours(1));

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

                _fileSystemWatcher.Changed += FileSystemChangedAsync;
                _fileSystemWatcher.Created += FileSystemChangedAsync;
                _fileSystemWatcher.Deleted += FileSystemChangedAsync;
                _fileSystemWatcher.Renamed += FileSystemChangedAsync;

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
                _fileSystemWatcher.Changed -= FileSystemChangedAsync;
                _fileSystemWatcher.Created -= FileSystemChangedAsync;
                _fileSystemWatcher.Deleted -= FileSystemChangedAsync;
                _fileSystemWatcher.Renamed -= FileSystemChangedAsync;
                _fileSystemWatcher.Dispose();
                _fileSystemWatcher = null;

                _logger.Log(LogLevel.Verbose, "Destroyed FileSystemWatcher - no longer monitoring {0}.", Source);
            }
        }


        /// <summary>
        /// This is an event handler for background work. Therefore, it should never throw exceptions.
        /// </summary>
        private async void FileSystemChangedAsync(object sender, FileSystemEventArgs e)
        {
            try
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
                    await AddPackagesFromDropFolderAsync(CancellationToken.None);
                }

                // 2) If a file is updated in a subdirectory, *or* a folder is deleted, invalidate the cache
                if ((!string.Equals(Path.GetDirectoryName(e.FullPath), _fileSystemWatcher.Path, StringComparison.OrdinalIgnoreCase) && File.Exists(e.FullPath))
                    || e.ChangeType == WatcherChangeTypes.Deleted)
                {
                    // TODO: invalidating *all* packages for every nupkg change under this folder seems more expensive than it should.
                    // Recommend using e.FullPath to figure out which nupkgs need to be (re)computed.

                    await ClearCacheAsync(CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                _logger.Log(LogLevel.Error, "An exception occurred while handling a file system event: {0}", exception);
            }
        }

        private async Task<Lock> LockAsync(CancellationToken token)
        {
            var handle = new Lock(_syncLock);
            await handle.WaitAsync(token);
            return handle;
        }

        private async Task<SuppressedFileSystemWatcher> LockAndSuppressFileSystemWatcherAsync(CancellationToken token)
        {
            var handle = new SuppressedFileSystemWatcher(this);
            await handle.WaitAsync(token);
            return handle;
        }

        /// <summary>
        /// A disposable type that wraps a semaphore so dispose releases the semaphore. This allows for more ergonomic
        /// used (such as in a <code>using</code> statement).
        /// </summary>
        private class Lock : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private bool _lockTaken;

            public Lock(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public bool LockTaken => _lockTaken;

            public async Task WaitAsync(CancellationToken token)
            {
                await _semaphore.WaitAsync(token);
                _lockTaken = true;
            }

            public void Dispose()
            {
                if (_lockTaken)
                {
                    _semaphore.Release();
                    _lockTaken = false;
                }
            }
        }

        private class SuppressedFileSystemWatcher : IDisposable
        {
            private readonly ServerPackageRepository _repository;
            private Lock _lockHandle;

            public SuppressedFileSystemWatcher(ServerPackageRepository repository)
            {
                if (repository == null)
                {
                    throw new ArgumentNullException(nameof(repository));
                }

                _repository = repository;
            }

            public bool LockTaken => _lockHandle.LockTaken;

            public async Task WaitAsync(CancellationToken token)
            {
                _lockHandle = await _repository.LockAsync(token);
                _repository._isFileSystemWatcherSuppressed = true;
            }

            public void Dispose()
            {
                if (_lockHandle != null && _lockHandle.LockTaken)
                {
                    _lockHandle.Dispose();
                    _repository._isFileSystemWatcherSuppressed = false;
                }
            }
        }
    }
}