// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.Core.Tests.Infrastructure;
using Xunit;

namespace NuGet.Server.Core.Tests
{
    public class ServerPackageRepositoryTest
    {
        private static CancellationToken Token => CancellationToken.None;

        public static async Task<ServerPackageRepository> CreateServerPackageRepositoryAsync(
            string path,
            Action<ExpandedPackageRepository> setupRepository = null,
            Func<string, bool, bool> getSetting = null)
        {
            var fileSystem = new PhysicalFileSystem(path);
            var expandedPackageRepository = new ExpandedPackageRepository(fileSystem);

            setupRepository?.Invoke(expandedPackageRepository);

            var serverRepository = new ServerPackageRepository(
                fileSystem,
                runBackgroundTasks: false,
                innerRepository: expandedPackageRepository,
                logger: new Infrastructure.NullLogger(),
                settingsProvider: getSetting != null ? new FuncSettingsProvider(getSetting) : null);

            await serverRepository.GetPackagesAsync(Token); // caches the files

            return serverRepository;
        }

        [Fact]
        public async Task ServerPackageRepositoryAddsPackagesFromDropFolderOnStart()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var packagesToAddToDropFolder = new Dictionary<string, IPackage>
                {
                    {"test.1.11.nupkg", CreatePackage("test", "1.11")},
                    {"test.1.9.nupkg", CreatePackage("test", "1.9")},
                    {"test.2.0-alpha.nupkg", CreatePackage("test", "2.0-alpha")},
                    {"test.2.0.0.nupkg", CreatePackage("test", "2.0.0")},
                    {"test.2.0.0-0test.nupkg", CreatePackage("test", "2.0.0-0test")},
                    {"test.2.0.0-test+tag.nupkg", CreatePackage("test", "2.0.0-test+tag")}
                };
                foreach (var packageToAddToDropFolder in packagesToAddToDropFolder)
                {
                    using (var stream = File.Create(
                        Path.Combine(temporaryDirectory.Path, packageToAddToDropFolder.Key)))
                    {
                        packageToAddToDropFolder.Value.GetStream().CopyTo(stream);
                    }
                }

                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path);

                // Act
                var packages = await serverRepository.GetPackagesAsync(Token);

                // Assert
                Assert.Equal(packagesToAddToDropFolder.Count, packages.Count());
                foreach (var packageToAddToDropFolder in packagesToAddToDropFolder)
                {
                    var package = packages.FirstOrDefault(
                            p => p.Id == packageToAddToDropFolder.Value.Id 
                                && p.Version == packageToAddToDropFolder.Value.Version);

                    // check the package from drop folder has been added
                    Assert.NotNull(package); 

                    // check the package in the drop folder has been removed
                    Assert.False(File.Exists(Path.Combine(temporaryDirectory.Path, packageToAddToDropFolder.Key)));
                }
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryRemovePackage()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "1.11"));
                    repository.AddPackage(CreatePackage("test", "1.9"));
                    repository.AddPackage(CreatePackage("test", "2.0-alpha"));
                    repository.AddPackage(CreatePackage("test", "2.0.0"));
                    repository.AddPackage(CreatePackage("test", "2.0.0-0test"));
                    repository.AddPackage(CreatePackage("test", "2.0.0-test+tag"));
                    repository.AddPackage(CreatePackage("test", "2.0.1+taggedOnly"));
                });

                // Act
                await serverRepository.RemovePackageAsync("test", new SemanticVersion("1.11"), Token);
                await serverRepository.RemovePackageAsync("test", new SemanticVersion("2.0-alpha"), Token);
                await serverRepository.RemovePackageAsync("test", new SemanticVersion("2.0.1"), Token);
                await serverRepository.RemovePackageAsync("test", new SemanticVersion("2.0.0-0test"), Token);
                var packages = await serverRepository.GetPackagesAsync(Token);

                // Assert
                Assert.Equal(3, packages.Count());
                Assert.Equal(1, packages.Count(p => p.IsLatestVersion));
                Assert.Equal("2.0.0", packages.First(p => p.IsLatestVersion).Version.ToString());

                Assert.Equal(1, packages.Count(p => p.IsAbsoluteLatestVersion));
                Assert.Equal("2.0.0", packages.First(p => p.IsAbsoluteLatestVersion).Version.ToString());
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryNeedsRebuild()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path,
                    repository =>
                    {
                        repository.AddPackage(CreatePackage("test", "1.0"));
                        repository.AddPackage(CreatePackage("test", "1.1"));
                    });

                // Act
                await serverRepository.ClearCacheAsync(Token);
                await serverRepository.AddPackageAsync(CreatePackage("test", "1.2"), Token);
                var packages = await serverRepository.GetPackagesAsync(Token);

                // Assert
                packages = packages.OrderBy(p => p.Version);

                Assert.Equal(3, packages.Count());
                Assert.Equal(new SemanticVersion("1.0.0"), packages.ElementAt(0).Version);
                Assert.Equal(new SemanticVersion("1.1.0"), packages.ElementAt(1).Version);
                Assert.Equal(new SemanticVersion("1.2.0"), packages.ElementAt(2).Version);
            }
        }

        [Fact]
        public async Task ServerPackageRepositorySearch()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "1.0"));
                    repository.AddPackage(CreatePackage("test2", "1.0"));
                    repository.AddPackage(CreatePackage("test3", "1.0-alpha"));
                    repository.AddPackage(CreatePackage("test3", "2.0.0"));
                    repository.AddPackage(CreatePackage("test4", "2.0"));
                    repository.AddPackage(CreatePackage("test5", "1.0.0-0test"));
                    repository.AddPackage(CreatePackage("test6", "1.2.3+taggedOnly"));
                });

                // Act
                var includePrerelease = await serverRepository.SearchAsync("test3", allowPrereleaseVersions: true, token: Token);
                var excludePrerelease = await serverRepository.SearchAsync("test3", allowPrereleaseVersions: false, token: Token);
                var ignoreTag = await serverRepository.SearchAsync("test6", allowPrereleaseVersions: false, token: Token);

                // Assert
                Assert.Equal("test3", includePrerelease.First().Id);
                Assert.Equal(2, includePrerelease.Count());
                Assert.Equal(1, excludePrerelease.Count());
                Assert.Equal("test6", ignoreTag.First().Id);
                Assert.Equal(1, ignoreTag.Count());
            }
        }

        [Fact]
        public async Task ServerPackageRepositorySearchUnlisted()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                Func<string, bool, bool> getSetting = (key, defaultValue) =>
                {
                    if (key == "enableDelisting")
                    {
                        return true;
                    }
                    return defaultValue;
                };

                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test1", "1.0"));
                }, getSetting);

                // Assert base setup
                var packages = (await serverRepository.SearchAsync("test1", allowPrereleaseVersions: true, token: Token)).ToList();
                Assert.Equal(1, packages.Count);
                Assert.Equal("test1", packages[0].Id);
                Assert.Equal("1.0", packages[0].Version.ToString());

                // Delist the package
                await serverRepository.RemovePackageAsync("test1", new SemanticVersion("1.0"), Token);

                // Verify that the package is not returned by search
                packages = (await serverRepository.SearchAsync("test1", allowPrereleaseVersions: true, token: Token)).ToList();
                Assert.Equal(0, packages.Count);

                // Act: search with includeDelisted=true
                packages = (await serverRepository.GetPackagesAsync(Token)).ToList();

                // Assert
                Assert.Equal(1, packages.Count);
                Assert.Equal("test1", packages[0].Id);
                Assert.Equal("1.0", packages[0].Version.ToString());
                Assert.False(packages[0].Listed);
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryFindPackageById()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "1.0"));
                    repository.AddPackage(CreatePackage("test2", "1.0"));
                    repository.AddPackage(CreatePackage("test3", "1.0-alpha"));
                    repository.AddPackage(CreatePackage("test4", "2.0"));
                    repository.AddPackage(CreatePackage("test4", "3.0.0+tagged"));
                    repository.AddPackage(CreatePackage("Not5", "4.0"));
                });

                // Act
                var valid = await serverRepository.FindPackagesByIdAsync("test", Token);
                var invalid = await serverRepository.FindPackagesByIdAsync("bad", Token);

                // Assert
                Assert.Equal("test", valid.First().Id);
                Assert.Equal(0, invalid.Count());
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryFindPackage()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "1.0"));
                    repository.AddPackage(CreatePackage("test2", "1.0"));
                    repository.AddPackage(CreatePackage("test3", "1.0.0-alpha"));
                    repository.AddPackage(CreatePackage("test4", "2.0"));
                    repository.AddPackage(CreatePackage("test4", "3.0.0+tagged"));
                    repository.AddPackage(CreatePackage("Not5", "4.0.0"));
                });

                // Act
                var valid = await serverRepository.FindPackageAsync("test4", new SemanticVersion("3.0.0"), Token);
                var valid2 = await serverRepository.FindPackageAsync("Not5", new SemanticVersion("4.0"), Token);
                var validPreRel = await serverRepository.FindPackageAsync("test3", new SemanticVersion("1.0.0-alpha"), Token);
                var invalidPreRel = await serverRepository.FindPackageAsync("test3", new SemanticVersion("1.0.0"), Token);
                var invalid = await serverRepository.FindPackageAsync("bad", new SemanticVersion("1.0"), Token);

                // Assert
                Assert.Equal("test4", valid.Id);
                Assert.Equal("Not5", valid2.Id);
                Assert.Equal("test3", validPreRel.Id);
                Assert.Null(invalidPreRel);
                Assert.Null(invalid);
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryMultipleIds()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "0.9"));
                    repository.AddPackage(CreatePackage("test", "1.0"));
                    repository.AddPackage(CreatePackage("test2", "1.0"));
                    repository.AddPackage(CreatePackage("test3", "1.0-alpha"));
                    repository.AddPackage(CreatePackage("test3", "2.0.0+taggedOnly"));
                    repository.AddPackage(CreatePackage("test4", "2.0"));
                    repository.AddPackage(CreatePackage("test4", "3.0.0"));
                    repository.AddPackage(CreatePackage("test5", "2.0.0-onlyPre+tagged"));
                });

                // Act
                var packages = await serverRepository.GetPackagesAsync(Token);

                // Assert
                Assert.Equal(5, packages.Count(p => p.IsAbsoluteLatestVersion));
                Assert.Equal(4, packages.Count(p => p.IsLatestVersion));
                Assert.Equal(3, packages.Count(p => !p.IsAbsoluteLatestVersion));
                Assert.Equal(4, packages.Count(p => !p.IsLatestVersion));
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryIsAbsoluteLatest()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "2.0-alpha"));
                    repository.AddPackage(CreatePackage("test", "2.1-alpha"));
                    repository.AddPackage(CreatePackage("test", "2.2-beta"));
                    repository.AddPackage(CreatePackage("test", "2.3"));
                    repository.AddPackage(CreatePackage("test", "2.4.0-prerel"));
                    repository.AddPackage(CreatePackage("test", "3.2.0+taggedOnly"));
                });

                // Act
                var packages = await serverRepository.GetPackagesAsync(Token);

                // Assert
                Assert.Equal(1, packages.Count(p => p.IsAbsoluteLatestVersion));
                Assert.Equal("3.2.0", packages.First(p => p.IsAbsoluteLatestVersion).Version.ToString());
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryIsLatestOnlyPreRel()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "2.0-alpha"));
                    repository.AddPackage(CreatePackage("test", "2.1-alpha"));
                    repository.AddPackage(CreatePackage("test", "2.2-beta+tagged"));
                });
                
                // Act
                var packages = await serverRepository.GetPackagesAsync(Token);

                // Assert
                Assert.Equal(0, packages.Count(p => p.IsLatestVersion));
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryIsLatest()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "1.11"));
                    repository.AddPackage(CreatePackage("test", "1.9"));
                    repository.AddPackage(CreatePackage("test", "2.0-alpha"));
                    repository.AddPackage(CreatePackage("test1", "1.0.0"));
                    repository.AddPackage(CreatePackage("test1", "1.2.0+taggedOnly"));
                    repository.AddPackage(CreatePackage("test1", "2.0.0-alpha"));
                });

                // Act
                var packages = await serverRepository.GetPackagesAsync(Token);
                var latestVersions = packages.Where(p => p.IsLatestVersion).Select(p => p.Version.ToString()).ToList();

                // Assert
                Assert.Equal(2, packages.Count(p => p.IsLatestVersion));
                Assert.Contains("1.11", latestVersions);
                Assert.Contains("1.2.0", latestVersions);
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryReadsDerivedData()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var package = CreatePackage("test", "1.0");
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(package);
                });

                // Act
                var packages = await serverRepository.GetPackagesAsync(Token);

                // Assert
                var singlePackage = packages.SingleOrDefault();
                Assert.NotNull(singlePackage);
                Assert.Equal(package.GetStream().Length, singlePackage.PackageSize);
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryEmptyRepo()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                CreatePackage("test", "1.0");
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path);

                // Act
                var findPackage = await serverRepository.FindPackageAsync("test", new SemanticVersion("1.0"), Token);
                var findPackagesById = await serverRepository.FindPackagesByIdAsync("test", Token);
                var getPackages = (await serverRepository.GetPackagesAsync(Token)).ToList();
                var getPackagesWithDerivedData = (await serverRepository.GetPackagesAsync(Token)).ToList();
                var getUpdates = await serverRepository.GetUpdatesAsync(
                    Enumerable.Empty<IPackageName>(),
                    includePrerelease: true,
                    includeAllVersions: true,
                    targetFramework: Enumerable.Empty<FrameworkName>(),
                    versionConstraints: Enumerable.Empty<IVersionSpec>(),
                    token: Token);
                var search = (await serverRepository.SearchAsync("test", allowPrereleaseVersions: true, token: Token)).ToList();
                var source = serverRepository.Source;

                // Assert
                Assert.Null(findPackage);
                Assert.Empty(findPackagesById);
                Assert.Empty(getPackages);
                Assert.Empty(getPackagesWithDerivedData);
                Assert.Empty(getUpdates);
                Assert.Empty(search);
                Assert.NotEmpty(source);
            }
        }
        
        private static IPackage CreateMockPackage(string id, string version)
        {
            var package = new Mock<IPackage>();
            package.Setup(p => p.Id).Returns(id);
            package.Setup(p => p.Version).Returns(new SemanticVersion(version));
            package.Setup(p => p.IsLatestVersion).Returns(true);
            package.Setup(p => p.Listed).Returns(true);

            return package.Object;
        }

        private IPackage CreatePackage(string id, string version)
        {
            var package = new PackageBuilder
            {
                Id = id,
                Version = new SemanticVersion(version),
                Description = "Description",
                Authors = { "Test Author" }
            };

            var mockFile = new Mock<IPackageFile>();
            mockFile.Setup(m => m.Path).Returns("foo");
            mockFile.Setup(m => m.GetStream()).Returns(new MemoryStream());
            package.Files.Add(mockFile.Object);

            var packageStream = new MemoryStream();
            package.Save(packageStream);
            packageStream.Seek(0, SeekOrigin.Begin);

            return new ZipPackage(packageStream);
        }
    }
}
