// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace NuGet.Server.Core.Infrastructure
{
    public class ServerPackageCache
        : IServerPackageCache
    {
        private readonly IPackagesSerializer _packagesSerializer = new JsonNetPackagesSerializer();
        
        private readonly ReaderWriterLockSlim _syncLock = new ReaderWriterLockSlim();

        private bool _isDirty;

        private readonly IFileSystem _fileSystem;
        private readonly string _fileName;

        private readonly HashSet<ServerPackage> _packages = new HashSet<ServerPackage>(IdAndVersionEqualityComparer.Instance);

        public ServerPackageCache(IFileSystem fileSystem, string fileName)
        {
            _fileSystem = fileSystem;
            _fileName = fileName;

            Load();
        }

        private void Load()
        {
            _syncLock.EnterWriteLock();
            try
            {
                if (_fileSystem.FileExists(_fileName))
                {
                    _packages.Clear();

                    try
                    {
                        using (var stream = _fileSystem.OpenFile(_fileName))
                        {
                            var deserializedPackages = _packagesSerializer.Deserialize(stream);
                            if (deserializedPackages != null)
                            {
                                _packages.AddRange(deserializedPackages);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is JsonException || ex is SerializationException)
                        {
                            // In case this happens, remove the file
                            _fileSystem.DeleteFile(_fileName);
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }
            finally
            {
                _syncLock.ExitWriteLock();
            }
        }

        public bool IsEmpty()
        {
            _syncLock.EnterReadLock();
            try
            {
                return _packages.Count == 0;
            }
            finally
            {
                _syncLock.ExitReadLock();
            }
        }

        public IEnumerable<ServerPackage> GetAll()
        {
            _syncLock.EnterReadLock();
            try
            {
                return _packages.ToList();
            }
            finally
            {
                _syncLock.ExitReadLock();
            }
        }

        public void Remove(string id, SemanticVersion version, bool enableDelisting)
        {
            _syncLock.EnterWriteLock();
            try
            {
                if (enableDelisting)
                {
                    foreach (var package in _packages.Where(p => IsMatch(p, id, version)))
                    {
                        package.Listed = false;
                    }
                }
                else
                {
                    _packages.RemoveWhere(p => IsMatch(p, id, version));
                }

                UpdateLatestVersions(_packages.Where(p => IsMatch(p, id)));

                _isDirty = true;
            }
            finally
            {
                _syncLock.ExitWriteLock();
            }
        }

        public void Add(ServerPackage entity)
        {
            _syncLock.EnterWriteLock();
            try
            {
                _packages.Remove(entity);
                _packages.Add(entity);

                UpdateLatestVersions(_packages.Where(p => IsMatch(p, entity.Id)));

                _isDirty = true;
            }
            finally
            {
                _syncLock.ExitWriteLock();
            }
        }

        public void AddRange(IEnumerable<ServerPackage> entities)
        {
            _syncLock.EnterWriteLock();
            try
            {
                foreach (var entity in entities)
                {
                    _packages.Remove(entity);
                    _packages.Add(entity);
                }

                UpdateLatestVersions(_packages);

                _isDirty = true;
            }
            finally
            {
                _syncLock.ExitWriteLock();
            }
        }

        private static void UpdateLatestVersions(IEnumerable<ServerPackage> packages)
        {
            var absoluteLatest = new ConcurrentDictionary<string, ServerPackage>();
            var latest = new ConcurrentDictionary<string, ServerPackage>();

            // Visit packages
            Parallel.ForEach(packages, package =>
            {
                // Update package
                package.IsAbsoluteLatestVersion = false;
                package.IsLatestVersion = false;

                // Find the latest versions
                var id = package.Id.ToLowerInvariant();

                // Update with the highest version
                absoluteLatest.AddOrUpdate(id, package,
                    (oldId, oldEntry) => oldEntry.Version < package.Version ? package : oldEntry);

                // Update latest for release versions
                if (package.IsReleaseVersion())
                {
                    latest.AddOrUpdate(id, package,
                        (oldId, oldEntry) => oldEntry.Version < package.Version ? package : oldEntry);
                }
            });

            // Set version properties
            foreach (var entry in absoluteLatest.Values)
            {
                entry.IsAbsoluteLatestVersion = true;
            }

            foreach (var entry in latest.Values)
            {
                entry.IsLatestVersion = true;
            }
        }

        public void Persist()
        {
            _syncLock.EnterWriteLock();
            try
            {
                using (var stream = _fileSystem.CreateFile(_fileName))
                {
                    _packagesSerializer.Serialize(_packages, stream);
                }

                _isDirty = false;
            }
            finally
            {
                _syncLock.ExitWriteLock();
            }
        }

        public void PersistIfDirty()
        {
            if (_isDirty)
            {
                Persist();
            }
        }

        public void Clear()
        {
            _syncLock.EnterWriteLock();
            try
            {
                _packages.Clear();

                _isDirty = true;
            }
            finally
            {
                _syncLock.ExitWriteLock();
            }
        }

        private static bool IsMatch(ServerPackage package, string id)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(package.Id, id);
        }

        private static bool IsMatch(ServerPackage package, string id, SemanticVersion version)
        {
            return IsMatch(package, id) && package.Version == version;
        }
    }
}