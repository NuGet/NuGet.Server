// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Server.Core.Logging;

namespace NuGet.Server.Core.Infrastructure
{
    public class ServerPackageStore
        : IServerPackageStore
    {
        private readonly IFileSystem _fileSystem;
        private readonly ExpandedPackageRepository _repository;
        private readonly Logging.ILogger _logger;

        public ServerPackageStore(
            IFileSystem fileSystem,
            ExpandedPackageRepository repository,
            Logging.ILogger logger)
        {
            _fileSystem = fileSystem;
            _repository = repository;
            _logger = logger;
        }

        public bool Exists(string id, SemanticVersion version)
        {
            return _repository.Exists(id, version);
        }

        public ServerPackage Add(IPackage package, bool enableDelisting)
        {
            _repository.AddPackage(package);

            return CreateServerPackage(package, enableDelisting);
        }

        public void Remove(string id, SemanticVersion version, bool enableDelisting)
        {
            if (enableDelisting)
            {
                var physicalFileSystem = _fileSystem as PhysicalFileSystem;

                if (physicalFileSystem != null)
                {
                    var fileName = physicalFileSystem.GetFullPath(
                        GetPackageFileName(id, version.ToNormalizedString()));

                    if (File.Exists(fileName))
                    {
                        File.SetAttributes(fileName, File.GetAttributes(fileName) | FileAttributes.Hidden);
                    }
                    else
                    {
                        _logger.Log(
                            LogLevel.Error,
                            "Error removing package {0} {1} - could not find package file {2}",
                            id,
                            version,
                            fileName);
                    }
                }
            }
            else
            {
                var package = _repository.FindPackage(id, version);

                if (package != null)
                {
                    _repository.RemovePackage(package);
                }
            }
        }

        public async Task<HashSet<ServerPackage>> GetAllAsync(bool enableDelisting, CancellationToken token)
        {
            var allPackages = new ConcurrentBag<ServerPackage>();

            var tasks = _repository
                .GetPackages()
                .Select(package => TryAddServerPackageAsync(allPackages, package, enableDelisting, token))
                .ToList();

            await Task.WhenAll(tasks);

            // Only return unique packages.
            return new HashSet<ServerPackage>(allPackages, IdAndVersionEqualityComparer.Instance);
        }

        private async Task TryAddServerPackageAsync(
            ConcurrentBag<ServerPackage> allPackages,
            IPackage package,
            bool enableDelisting,
            CancellationToken token)
        {
            // Immediately defer work to the background thread.
            await Task.Yield();

            // Try to create the server package and ignore a bad package if it fails.
            var serverPackage = CreateServerPackage(package, enableDelisting, token);
            if (serverPackage != null)
            {
                allPackages.Add(serverPackage);
            }
        }

        private ServerPackage CreateServerPackage(
            IPackage package,
            bool enableDelisting,
            CancellationToken token)
        {
            try
            {
                return CreateServerPackage(package, enableDelisting);
            }
            catch (Exception e)
            {
                _logger.Log(
                    LogLevel.Warning,
                    "Unable to create server package - {0} {1}: {2}",
                    package.Id,
                    package.Version,
                    e.Message);

                return null;
            }
        }

        private ServerPackage CreateServerPackage(
            IPackage package,
            bool enableDelisting)
        {
            var packageDerivedData = GetPackageDerivedData(package, enableDelisting);
            
            var serverPackage = new ServerPackage(
                package,
                packageDerivedData);

            serverPackage.IsAbsoluteLatestVersion = false;
            serverPackage.IsLatestVersion = false;

            return serverPackage;
        }

        private PackageDerivedData GetPackageDerivedData(IPackage package, bool enableDelisting)
        {
            // File names
            var normalizedVersion = package.Version.ToNormalizedString();
            var packageFileName = GetPackageFileName(package.Id, normalizedVersion);
            var hashFileName = GetHashFileName(package.Id, normalizedVersion);

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
                var fullPath = _fileSystem.GetFullPath(packageFileName);
                var fileInfo = new FileInfo(fullPath);
                packageDerivedData.PackageSize = fileInfo.Length;

                packageDerivedData.LastUpdated = _fileSystem.GetLastModified(packageFileName);
                packageDerivedData.Created = _fileSystem.GetCreated(packageFileName);
                packageDerivedData.FullPath = fullPath;

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

            return packageDerivedData;
        }

        private static string GetPackageFileName(string id, string normalizedVersion)
        {
            return Path.Combine(
                id,
                normalizedVersion,
                $"{id}.{normalizedVersion}.nupkg");
        }

        private static string GetHashFileName(string id, string normalizedVersion)
        {
            return Path.Combine(
                id,
                normalizedVersion,
                $"{id}.{normalizedVersion}{NuGet.Constants.HashFileExtension}");
        }
    }
}
