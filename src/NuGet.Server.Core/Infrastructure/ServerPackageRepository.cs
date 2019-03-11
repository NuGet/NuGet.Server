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
        private string _watchDirectory;
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
            _serverPackageCache = InitializeServerPackageCache();
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
            if (innerRepository == null)
            {
                throw new ArgumentNullException(nameof(innerRepository));
            }

            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _runBackgroundTasks = runBackgroundTasks;
            _settingsProvider = settingsProvider ?? new DefaultSettingsProvider();
            _logger = logger ?? new TraceLogger();
            _serverPackageCache = InitializeServerPackageCache();
            _serverPackageStore = new ServerPackageStore(
                _fileSystem,
                innerRepository,
                _logger);
        }

        public string Source => _fileSystem.Root;

        private bool AllowOverrideExistingPackageOnPush =>
            _settingsProvider.GetBoolSetting("allowOverrideExistingPackageOnPush", false);

        private bool IgnoreSymbolsPackages =>
            _settingsProvider.GetBoolSetting("ignoreSymbolsPackages", false);

        private bool EnableDelisting =>
            _settingsProvider.GetBoolSetting("enableDelisting", false);

        private bool EnableFrameworkFiltering =>
            _settingsProvider.GetBoolSetting("enableFrameworkFiltering", false);

        private bool EnableFileSystemMonitoring =>
            _settingsProvider.GetBoolSetting("enableFileSystemMonitoring", true);

        private string CacheFileName => _settingsProvider.GetStringSetting("cacheFileName", null);

        private TimeSpan InitialCacheRebuildAfter
        {
            get
            {
                var value = GetPositiveIntSetting("initialCacheRebuildAfterSeconds", 15);
                return TimeSpan.FromSeconds(value);
            }
        }

        private TimeSpan CacheRebuildFrequency
        {
            get
            {
                int value = GetPositiveIntSetting("cacheRebuildFrequencyInMinutes", 60);
                return TimeSpan.FromMinutes(value);
            }
        }

        private int GetPositiveIntSetting(string name, int defaultValue)
        {
            var value = _settingsProvider.GetIntSetting(name, defaultValue);
            if (value <= 0)
            {
                value = defaultValue;
            }

            return value;
        }

        private ServerPackageCache InitializeServerPackageCache()
        {
            return new ServerPackageCache(_fileSystem, ResolveCacheFileName());
        }

        private string ResolveCacheFileName()
        {
            var fileName = CacheFileName;
            const string suffix = ".cache.bin";

            if (String.IsNullOrWhiteSpace(fileName))
            {
                // Default file name
                return Environment.MachineName.ToLowerInvariant() + suffix;
            }

            if (fileName.LastIndexOfAny(Path.GetInvalidFileNameChars()) > 0)
            {
                var message = string.Format(Strings.Error_InvalidCacheFileName, fileName);

                _logger.Log(LogLevel.Error, message);

                throw new InvalidOperationException(message);
            }

            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }

            return fileName + suffix;
        }

        /// <summary>
        /// Package cache containing packages metadata. 
        /// This data is generated if it does not exist already.
        /// </summary>
        public async Task<IEnumerable<IServerPackage>> GetPackagesAsync(
            ClientCompatibility compatibility,
            CancellationToken token)
        {
            await RebuildIfNeededAsync(shouldLock: true, token: token);

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

            var cache = _serverPackageCache.GetAll();

            if (!compatibility.AllowSemVer2)
            {
                cache = cache.Where(p => !p.IsSemVer2);
            }

            return cache;
        }

        private async Task RebuildIfNeededAsync(bool shouldLock, CancellationToken token)
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
                if (shouldLock)
                {
                    using (await LockAndSuppressFileSystemWatcherAsync(token))
                    {
                        // Check the flags again, just in case another thread already did this work.
                        if (_needsRebuild || _serverPackageCache.IsEmpty())
                        {
                            await RebuildPackageStoreWithoutLockingAsync(token);
                        }
                    }
                }
                else
                {
                    await RebuildPackageStoreWithoutLockingAsync(token);
                }
            }
        }

        public async Task<IEnumerable<IServerPackage>> SearchAsync(
            string searchTerm,
            IEnumerable<string> targetFrameworks,
            bool allowPrereleaseVersions,
            ClientCompatibility compatibility,
            CancellationToken token)
        {
            return await SearchAsync(searchTerm, targetFrameworks, allowPrereleaseVersions, false, compatibility, token);
        }

        public async Task<IEnumerable<IServerPackage>> SearchAsync(
            string searchTerm,
            IEnumerable<string> targetFrameworks,
            bool allowPrereleaseVersions,
            bool allowUnlistedVersions,
            ClientCompatibility compatibility,
            CancellationToken token)
        {
            var cache = await GetPackagesAsync(compatibility, token);

            var packages = cache
                .Find(searchTerm)
                .FilterByPrerelease(allowPrereleaseVersions);

            if (EnableDelisting && !allowUnlistedVersions)
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

        internal async Task AddPackagesFromDropFolderAsync(CancellationToken token)
        {
            using (await LockAndSuppressFileSystemWatcherAsync(token))
            {
                await RebuildIfNeededAsync(shouldLock: false, token: token);

                AddPackagesFromDropFolderWithoutLocking();
            }
        }

        /// <summary>
        /// This method requires <see cref="LockAndSuppressFileSystemWatcherAsync(CancellationToken)"/>.
        /// </summary>
        private void AddPackagesFromDropFolderWithoutLocking()
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
                        var package = PackageFactory.Open(_fileSystem.GetFullPath(packageFile));

                        if (!CanPackageBeAddedWithoutLocking(package, shouldThrow: false))
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
                _serverPackageCache.AddRange(serverPackages, EnableDelisting);
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

            using (await LockAndSuppressFileSystemWatcherAsync(token))
            {
                await RebuildIfNeededAsync(shouldLock: false, token: token);

                CanPackageBeAddedWithoutLocking(package, shouldThrow: true);

                // Add the package to the file system store.
                var serverPackage = _serverPackageStore.Add(
                    package,
                    EnableDelisting);

                // Add the package to the metadata store.
                _serverPackageCache.Add(serverPackage, EnableDelisting);

                _logger.Log(LogLevel.Info, "Finished adding package {0} {1}.", package.Id, package.Version);
            }
        }

        private bool CanPackageBeAddedWithoutLocking(IPackage package, bool shouldThrow)
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
            if (!AllowOverrideExistingPackageOnPush &&
                _serverPackageCache.Exists(package.Id, package.Version))
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
            _serverPackageCache.AddRange(packages, EnableDelisting);

            // Add packages from drop folder
            AddPackagesFromDropFolderWithoutLocking();

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
            _logger.Log(LogLevel.Info, "Persisting the cache file every 1 minute.");
            _persistenceTimer = new Timer(
                callback: state => _serverPackageCache.PersistIfDirty(),
                state: null,
                dueTime: TimeSpan.FromMinutes(1),
                period: TimeSpan.FromMinutes(1));

            // Rebuild the package store in the background
            _logger.Log(LogLevel.Info, "Rebuilding the cache file for the first time after {0} second(s).", InitialCacheRebuildAfter.TotalSeconds);
            _logger.Log(LogLevel.Info, "Rebuilding the cache file every {0} hour(s).", CacheRebuildFrequency.TotalHours);
            _rebuildTimer = new Timer(
                callback: state => RebuildPackageStoreAsync(CancellationToken.None),
                state: null,
                dueTime: InitialCacheRebuildAfter,
                period: CacheRebuildFrequency);

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
                _fileSystemWatcher = new FileSystemWatcher(Source)
                {
                    Filter = "*",
                    IncludeSubdirectories = true,
                };

                //Keep the normalized watch path.
                _watchDirectory = Path.GetFullPath(_fileSystemWatcher.Path);

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

            _watchDirectory = null;
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

                if (ShouldIgnoreFileSystemEvent(e))
                {
                    _logger.Log(LogLevel.Verbose, "File system event ignored. File: {0} - Change: {1}", e.Name, e.ChangeType);
                    return;
                }

                _logger.Log(LogLevel.Verbose, "File system changed. File: {0} - Change: {1}", e.Name, e.ChangeType);

                var changedDirectory = Path.GetDirectoryName(e.FullPath);
                if (changedDirectory == null || _watchDirectory == null)
                {
                    return;
                }

                changedDirectory = Path.GetFullPath(changedDirectory);

                // 1) If a .nupkg is dropped in the root, add it as a package
                if (string.Equals(changedDirectory, _watchDirectory, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetExtension(e.Name), ".nupkg", StringComparison.OrdinalIgnoreCase))
                {
                    // When a package is dropped into the server packages root folder, add it to the repository.
                    await AddPackagesFromDropFolderAsync(CancellationToken.None);
                }

                // 2) If a file is updated in a subdirectory, *or* a folder is deleted, invalidate the cache
                if ((!string.Equals(changedDirectory, _watchDirectory, StringComparison.OrdinalIgnoreCase) && File.Exists(e.FullPath))
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

        private bool ShouldIgnoreFileSystemEvent(FileSystemEventArgs e)
        {
            // We can only ignore Created or Changed events. All other types are always processed. Eventually we could
            // try to ignore some Deleted events in the case of API package delete, but this is harder.
            if (e.ChangeType != WatcherChangeTypes.Created
                && e.ChangeType != WatcherChangeTypes.Changed)
            {
                _logger.Log(LogLevel.Verbose, "The file system event change type is not ignorable.");
                return false;
            }

            /// We can only ignore events related to file paths changed by the
            /// <see cref="ExpandedPackageRepository"/>. If the file system event is representing a known file path
            /// extracted during package push, we can ignore the event. File system events are supressed during package
            /// push but this is still necessary since file system events can come some time after the suppression
            /// window has ended.
            if (!KnownPathUtility.TryParseFileName(e.Name, out var id, out var version))
            {
                _logger.Log(LogLevel.Verbose, "The file system event is not related to a known package path.");
                return false;
            }

            /// The file path could have been generated by <see cref="ExpandedPackageRepository"/>. Now
            /// determine if the package is in the cache.
            var matchingPackage = _serverPackageCache
                .GetAll()
                .Where(p => StringComparer.OrdinalIgnoreCase.Equals(p.Id, id))
                .Where(p => version.Equals(p.Version))
                .FirstOrDefault();

            if (matchingPackage == null)
            {
                _logger.Log(LogLevel.Verbose, "The file system event is not related to a known package.");
                return false;
            }

            var fileInfo = new FileInfo(e.FullPath);
            if (!fileInfo.Exists)
            {
                _logger.Log(LogLevel.Verbose, "The package file is missing.");
                return false;
            }

            var minimumCreationTime = DateTimeOffset.UtcNow.AddMinutes(-1);
            if (fileInfo.CreationTimeUtc < minimumCreationTime)
            {
                _logger.Log(LogLevel.Verbose, "The package file was not created recently.");
                return false;
            }

            return true;
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
        private sealed class Lock : IDisposable
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

        private sealed class SuppressedFileSystemWatcher : IDisposable
        {
            private readonly ServerPackageRepository _repository;
            private Lock _lockHandle;

            public SuppressedFileSystemWatcher(ServerPackageRepository repository)
            {
                _repository = repository ?? throw new ArgumentNullException(nameof(repository));
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
                    _repository._isFileSystemWatcherSuppressed = false;
                    _lockHandle.Dispose();
                }
            }
        }
    }
}