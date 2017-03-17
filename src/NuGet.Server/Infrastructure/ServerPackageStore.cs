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

namespace NuGet.Server.Infrastructure
{
    public class ServerPackageStore
        : IServerPackageStore
    {
        private readonly IPackagesSerializer _packagesSerializer = new JsonNetPackagesSerializer();
        
        private readonly ReaderWriterLockSlim _syncLock = new ReaderWriterLockSlim();

        private bool _isDirty;

        private readonly IFileSystem _fileSystem;
        private readonly string _fileName;

        private HashSet<ServerPackage> _packages = new HashSet<ServerPackage>(PackageEqualityComparer.IdAndVersion);

        public ServerPackageStore(IFileSystem fileSystem, string fileName)
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

        public bool HasPackages()
        {
            return GetAll().Any();
        }

        public IQueryable<ServerPackage> GetAll()
        {
            _syncLock.EnterReadLock();
            try
            {
                return _packages.ToList().AsQueryable();
            }
            finally
            {
                _syncLock.ExitReadLock();
            }
        }
        
        public void Remove(ServerPackage entity)
        {
            _syncLock.EnterWriteLock();
            try
            {
                _packages.Remove(entity);

                UpdateLatestVersions(_packages.Where(package =>
                    String.Equals(package.Id, entity.Id, StringComparison.OrdinalIgnoreCase)));

                _isDirty = true;
            }
            finally
            {
                _syncLock.ExitWriteLock();
            }
        }

        public void Remove(string id, SemanticVersion version)
        {
            _syncLock.EnterWriteLock();
            try
            {
                _packages.RemoveWhere(package =>
                    String.Equals(package.Id, id, StringComparison.OrdinalIgnoreCase) && package.Version == version);

                UpdateLatestVersions(_packages);

                _isDirty = true;
            }
            finally
            {
                _syncLock.ExitWriteLock();
            }
        }

        public void Store(ServerPackage entity)
        {
            _syncLock.EnterWriteLock();
            try
            {
                _packages.Remove(entity);
                _packages.Add(entity);

                UpdateLatestVersions(_packages.Where(package =>
                    String.Equals(package.Id, entity.Id, StringComparison.OrdinalIgnoreCase)));

                _isDirty = true;
            }
            finally
            {
                _syncLock.ExitWriteLock();
            }
        }

        public void StoreRange(IEnumerable<ServerPackage> entities)
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
            var semVer1AbsoluteLatest = InitializePackageDictionary();
            var semVer1Latest = InitializePackageDictionary();
            var semVer2AbsoluteLatest = InitializePackageDictionary();
            var semVer2Latest = InitializePackageDictionary();

            // Visit packages
            Parallel.ForEach(packages, package =>
            {
                // Reset the package.
                package.SemVer1IsAbsoluteLatest = false;
                package.SemVer1IsLatest = false;
                package.SemVer2IsAbsoluteLatest = false;
                package.SemVer2IsLatest = false;

                // Update the SemVer1 views.
                if (!package.IsSemVer2)
                {
                    UpdateLatestDictionary(semVer1AbsoluteLatest, package);

                    if (package.IsReleaseVersion())
                    {
                        UpdateLatestDictionary(semVer1Latest, package);
                    }
                }

                // Update the SemVer1 + SemVer2 views.
                UpdateLatestDictionary(semVer2AbsoluteLatest, package);

                if (package.IsReleaseVersion())
                {
                    UpdateLatestDictionary(semVer2Latest, package);
                }
            });

            // Set version properties
            foreach (var entry in semVer1AbsoluteLatest.Values)
            {
                entry.SemVer1IsAbsoluteLatest = true;
            }

            foreach (var entry in semVer1Latest.Values)
            {
                entry.SemVer1IsLatest = true;
            }

            foreach (var entry in semVer2AbsoluteLatest.Values)
            {
                entry.SemVer2IsAbsoluteLatest = true;
            }

            foreach (var entry in semVer2Latest.Values)
            {
                entry.SemVer2IsLatest = true;
            }
        }

        private static ConcurrentDictionary<string, ServerPackage> InitializePackageDictionary()
        {
            return new ConcurrentDictionary<string, ServerPackage>(StringComparer.OrdinalIgnoreCase);
        }

        private static void UpdateLatestDictionary(
            ConcurrentDictionary<string, ServerPackage> dictionary,
            ServerPackage package)
        {
            dictionary.AddOrUpdate(
                package.Id,
                package,
                (oldId, oldEntry) => oldEntry.Version < package.Version ? package : oldEntry);
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
    }
}