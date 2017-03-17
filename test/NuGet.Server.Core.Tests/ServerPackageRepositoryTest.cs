// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

            await serverRepository.GetPackagesAsync(ClientCompatibility.Max, Token); // caches the files

            return serverRepository;
        }

        private async Task<ServerPackageRepository> CreateServerPackageRepositoryWithSemVer2Async(
            TemporaryDirectory temporaryDirectory)
        {
            return await CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
            {
                repository.AddPackage(CreatePackage("test1", "1.0"));
                repository.AddPackage(CreatePackage("test2", "1.0-beta"));
                repository.AddPackage(CreatePackage("test3", "1.0-beta.1"));
                repository.AddPackage(CreatePackage("test4", "1.0-beta+foo"));
                repository.AddPackage(CreatePackage(
                    "test5",
                    "1.0-beta",
                    new PackageDependency(
                        "SomePackage",
                        VersionUtility.ParseVersionSpec("1.0.0-beta.1"))));
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ServerPackageRepositoryAddsPackagesFromDropFolderOnStart(bool allowOverride)
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

                var serverRepository = await CreateServerPackageRepositoryAsync(
                    temporaryDirectory.Path,
                    getSetting: (key, defaultValue) =>
                    {
                        if (key == "allowOverrideExistingPackageOnPush")
                        {
                            return allowOverride;
                        }

                        return defaultValue;
                    });

                // Act
                var packages = await serverRepository.GetPackagesAsync(ClientCompatibility.Max, Token);

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
                var packages = await serverRepository.GetPackagesAsync(ClientCompatibility.Max, Token);

                // Assert
                Assert.Equal(3, packages.Count());
                Assert.Equal(1, packages.Count(p => p.SemVer2IsLatest));
                Assert.Equal("2.0.0", packages.First(p => p.SemVer2IsLatest).Version.ToString());

                Assert.Equal(1, packages.Count(p => p.SemVer2IsAbsoluteLatest));
                Assert.Equal("2.0.0", packages.First(p => p.SemVer2IsAbsoluteLatest).Version.ToString());
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryNeedsRebuildIsHandledWhenAddingAfterClear()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path);

                // Act
                await serverRepository.ClearCacheAsync(Token);
                await serverRepository.AddPackageAsync(CreatePackage("test", "1.2"), Token);
                var packages = await serverRepository.GetPackagesAsync(ClientCompatibility.Max, Token);

                // Assert
                packages = packages.OrderBy(p => p.Version);

                Assert.Equal(1, packages.Count());
                Assert.Equal(new SemanticVersion("1.2.0"), packages.ElementAt(0).Version);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ServerPackageRepository_DuplicateAddAfterClearObservesOverrideOption(bool allowOverride)
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(
                    temporaryDirectory.Path,
                    getSetting: (key, defaultValue) =>
                    {
                        if (key == "allowOverrideExistingPackageOnPush")
                        {
                            return allowOverride;
                        }

                        return defaultValue;
                    });

                await serverRepository.AddPackageAsync(CreatePackage("test", "1.2"), Token);
                await serverRepository.ClearCacheAsync(Token);

                // Act & Assert
                if (allowOverride)
                {
                    await serverRepository.AddPackageAsync(CreatePackage("test", "1.2"), Token);
                }
                else
                {
                    await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await serverRepository.AddPackageAsync(CreatePackage("test", "1.2"), Token));
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ServerPackageRepository_DuplicateInDropFolderAfterClearObservesOverrideOption(bool allowOverride)
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(
                    temporaryDirectory.Path,
                    getSetting: (key, defaultValue) =>
                    {
                        if (key == "allowOverrideExistingPackageOnPush")
                        {
                            return allowOverride;
                        }

                        return defaultValue;
                    });

                await serverRepository.AddPackageAsync(CreatePackage("test", "1.2"), Token);
                var existingPackage = await serverRepository.FindPackageAsync(
                    "test",
                    new SemanticVersion("1.2"),
                    Token);
                var dropFolderPackagePath = Path.Combine(temporaryDirectory, "test.nupkg");
                await serverRepository.ClearCacheAsync(Token);

                // Act
                File.Copy(existingPackage.FullPath, dropFolderPackagePath);
                await serverRepository.AddPackagesFromDropFolderAsync(Token);

                // Assert
                Assert.NotEqual(allowOverride, File.Exists(dropFolderPackagePath));
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
                var includePrerelease = await serverRepository.SearchAsync(
                    "test3",
                    allowPrereleaseVersions: true,
                    compatibility: ClientCompatibility.Max,
                    token: Token);
                var excludePrerelease = await serverRepository.SearchAsync(
                    "test3",
                    allowPrereleaseVersions: false,
                    compatibility: ClientCompatibility.Max,
                    token: Token);
                var ignoreTag = await serverRepository.SearchAsync(
                    "test6",
                    allowPrereleaseVersions: false,
                    compatibility: ClientCompatibility.Max,
                    token: Token);

                // Assert
                Assert.Equal("test3", includePrerelease.First().Id);
                Assert.Equal(2, includePrerelease.Count());
                Assert.Equal(1, excludePrerelease.Count());
                Assert.Equal("test6", ignoreTag.First().Id);
                Assert.Equal(1, ignoreTag.Count());
            }
        }

        [Fact]
        public async Task ServerPackageRepositorySearchSupportsFilteringOutSemVer2()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryWithSemVer2Async(temporaryDirectory);

                // Act
                var actual = await serverRepository.SearchAsync(
                    "test",
                    targetFrameworks: Enumerable.Empty<string>(),
                    allowPrereleaseVersions: true,
                    compatibility: ClientCompatibility.Default,
                    token: Token);

                // Assert
                var packages = actual.OrderBy(p => p.Id).ToList();
                Assert.Equal(2, packages.Count);
                Assert.Equal("test1", packages[0].Id);
                Assert.Equal("test2", packages[1].Id);
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
                var packages = (await serverRepository.SearchAsync(
                    "test1",
                    allowPrereleaseVersions: true,
                    compatibility: ClientCompatibility.Max,
                    token: Token)).ToList();
                Assert.Equal(1, packages.Count);
                Assert.Equal("test1", packages[0].Id);
                Assert.Equal("1.0", packages[0].Version.ToString());

                // Delist the package
                await serverRepository.RemovePackageAsync("test1", new SemanticVersion("1.0"), Token);

                // Verify that the package is not returned by search
                packages = (await serverRepository.SearchAsync(
                    "test1",
                    allowPrereleaseVersions: true,
                    compatibility: ClientCompatibility.Max,
                    token: Token)).ToList();
                Assert.Equal(0, packages.Count);

                // Act: search with includeDelisted=true
                packages = (await serverRepository.GetPackagesAsync(ClientCompatibility.Max, Token)).ToList();

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
                var valid = await serverRepository.FindPackagesByIdAsync("test", ClientCompatibility.Max, Token);
                var invalid = await serverRepository.FindPackagesByIdAsync("bad", ClientCompatibility.Max, Token);

                // Assert
                Assert.Equal("test", valid.First().Id);
                Assert.Equal(0, invalid.Count());
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryFindPackageByIdSupportsFilteringOutSemVer2()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryWithSemVer2Async(temporaryDirectory);

                // Act
                var actual = await serverRepository.FindPackagesByIdAsync("test3", ClientCompatibility.Default, Token);

                // Assert
                Assert.Empty(actual);
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryGetPackagesSupportsFilteringOutSemVer2()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryWithSemVer2Async(temporaryDirectory);

                // Act
                var actual = await serverRepository.GetPackagesAsync(ClientCompatibility.Default, Token);

                // Assert
                var packages = actual.OrderBy(p => p.Id).ToList();
                Assert.Equal(2, packages.Count);
                Assert.Equal("test1", packages[0].Id);
                Assert.Equal("test2", packages[1].Id);
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryGetPackagesSupportsIncludingSemVer2()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryWithSemVer2Async(temporaryDirectory);

                // Act
                var actual = await serverRepository.GetPackagesAsync(ClientCompatibility.Max, Token);

                // Assert
                var packages = actual.OrderBy(p => p.Id).ToList();
                Assert.Equal(5, packages.Count);
                Assert.Equal("test1", packages[0].Id);
                Assert.Equal("test2", packages[1].Id);
                Assert.Equal("test3", packages[2].Id);
                Assert.Equal("test4", packages[3].Id);
                Assert.Equal("test5", packages[4].Id);
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
                var validPreRel = await serverRepository.FindPackageAsync(
                    "test3",
                    new SemanticVersion("1.0.0-alpha"),
                    Token);
                var invalidPreRel = await serverRepository.FindPackageAsync(
                    "test3", 
                    new SemanticVersion("1.0.0"),
                    Token);
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
                var packages = await serverRepository.GetPackagesAsync(ClientCompatibility.Max, Token);

                // Assert
                Assert.Equal(5, packages.Count(p => p.SemVer2IsAbsoluteLatest));
                Assert.Equal(4, packages.Count(p => p.SemVer2IsLatest));
                Assert.Equal(3, packages.Count(p => !p.SemVer2IsAbsoluteLatest));
                Assert.Equal(4, packages.Count(p => !p.SemVer2IsLatest));
            }
        }

        [Fact]
        public async Task ServerPackageRepositorySemVer1IsAbsoluteLatest()
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
                var packages = await serverRepository.GetPackagesAsync(ClientCompatibility.Default, Token);

                // Assert
                Assert.Equal(1, packages.Count(p => p.SemVer1IsAbsoluteLatest));
                Assert.Equal("2.4.0-prerel", packages.First(p => p.SemVer1IsAbsoluteLatest).Version.ToString());
            }
        }

        [Fact]
        public async Task ServerPackageRepositorySemVer2IsAbsoluteLatest()
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
                var packages = await serverRepository.GetPackagesAsync(ClientCompatibility.Max, Token);

                // Assert
                Assert.Equal(1, packages.Count(p => p.SemVer2IsAbsoluteLatest));
                Assert.Equal("3.2.0", packages.First(p => p.SemVer2IsAbsoluteLatest).Version.ToString());
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
                var packages = await serverRepository.GetPackagesAsync(ClientCompatibility.Max, Token);

                // Assert
                Assert.Equal(0, packages.Count(p => p.SemVer2IsLatest));
            }
        }

        [Fact]
        public async Task ServerPackageRepositorySemVer1IsLatest()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test1", "1.0.0"));
                    repository.AddPackage(CreatePackage("test1", "1.2.0+taggedOnly"));
                    repository.AddPackage(CreatePackage("test1", "2.0.0-alpha"));
                });

                // Act
                var packages = await serverRepository.GetPackagesAsync(ClientCompatibility.Default, Token);

                // Assert
                Assert.Equal(1, packages.Count(p => p.SemVer1IsLatest));
                Assert.Equal("1.0.0", packages.First(p => p.SemVer1IsLatest).Version.ToString());
            }
        }

        [Fact]
        public async Task ServerPackageRepositorySemVer2IsLatest()
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
                var packages = await serverRepository.GetPackagesAsync(ClientCompatibility.Max, Token);
                var latestVersions = packages.Where(p => p.SemVer2IsLatest).Select(p => p.Version.ToString()).ToList();

                // Assert
                Assert.Equal(2, packages.Count(p => p.SemVer2IsLatest));
                Assert.Equal("1.11", packages
                    .OrderBy(p => p.Id)
                    .First(p => p.SemVer2IsLatest)
                    .Version
                    .ToString());
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
                var packages = await serverRepository.GetPackagesAsync(ClientCompatibility.Max, Token);

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
                var findPackagesById = await serverRepository.FindPackagesByIdAsync(
                    "test",
                    ClientCompatibility.Max,
                    Token);
                var getPackages = (await serverRepository.GetPackagesAsync(
                    ClientCompatibility.Max,
                    Token)).ToList();
                var getPackagesWithDerivedData = (await serverRepository.GetPackagesAsync(
                    ClientCompatibility.Max,
                    Token)).ToList();
                var getUpdates = await serverRepository.GetUpdatesAsync(
                    Enumerable.Empty<IPackageName>(),
                    includePrerelease: true,
                    includeAllVersions: true,
                    targetFramework: Enumerable.Empty<FrameworkName>(),
                    versionConstraints: Enumerable.Empty<IVersionSpec>(),
                    compatibility: ClientCompatibility.Max,
                    token: Token);
                var search = (await serverRepository.SearchAsync(
                    "test",
                    allowPrereleaseVersions: true,
                    compatibility: ClientCompatibility.Max,
                    token: Token)).ToList();
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

        [Fact]
        public async Task ServerPackageRepositoryAddPackage()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path);

                // Act
                await serverRepository.AddPackageAsync(CreatePackage("Foo", "1.0.0"), Token);

                // Assert
                Assert.True(await serverRepository.ExistsAsync("Foo", new SemanticVersion("1.0.0"), Token));
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryAddPackageSemVer2()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path);

                // Act
                await serverRepository.AddPackageAsync(CreatePackage("Foo", "1.0.0+foo"), Token);

                // Assert
                Assert.True(await serverRepository.ExistsAsync("Foo", new SemanticVersion("1.0.0"), Token));
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryRemovePackageSemVer2()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(temporaryDirectory.Path);
                await serverRepository.AddPackageAsync(CreatePackage("Foo", "1.0.0+foo"), Token);

                // Act
                await serverRepository.RemovePackageAsync("Foo", new SemanticVersion("1.0.0+bar"), Token);

                // Assert
                Assert.False(await serverRepository.ExistsAsync("Foo", new SemanticVersion("1.0.0"), Token));
            }
        }

        [Fact]
        public async Task ServerPackageRepositoryAddPackageRejectsDuplicatesWithSemVer2()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = await CreateServerPackageRepositoryAsync(
                    temporaryDirectory.Path,
                    getSetting: (key, defaultValue) =>
                    {
                        if (key == "allowOverrideExistingPackageOnPush")
                        {
                            return false;
                        }

                        return defaultValue;
                    });
                await serverRepository.AddPackageAsync(CreatePackage("Foo", "1.0.0-beta.1+foo"), Token);

                // Act & Assert
                var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await serverRepository.AddPackageAsync(CreatePackage("Foo", "1.0.0-beta.1+bar"), Token));
                Assert.Equal(
                    "Package Foo 1.0.0-beta.1 already exists. The server is configured to not allow overwriting packages that already exist.",
                    actual.Message);
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

        private IPackage CreatePackage(string id, string version, PackageDependency packageDependency = null)
        {
            var parsedVersion = new SemanticVersion(version);
            var packageBuilder = new PackageBuilder
            {
                Id = id,
                Version = parsedVersion,
                Description = "Description",
                Authors = { "Test Author" }
            };

            if (packageDependency != null)
            {
                packageBuilder.DependencySets.Add(new PackageDependencySet(
                    new FrameworkName(".NETFramework,Version=v4.5"),
                    new[]
                    {
                        packageDependency
                    }));
            }

            var mockFile = new Mock<IPackageFile>();
            mockFile.Setup(m => m.Path).Returns("foo");
            mockFile.Setup(m => m.GetStream()).Returns(new MemoryStream());
            packageBuilder.Files.Add(mockFile.Object);

            var packageStream = new MemoryStream();
            packageBuilder.Save(packageStream);

            // NuGet.Core package builder strips SemVer2 metadata when saving the output package. Fix up the version
            // in the actual manifest.
            packageStream.Seek(0, SeekOrigin.Begin);
            using (var zipArchive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
            {
                var manifestFile = zipArchive
                    .Entries
                    .First(f => Path.GetExtension(f.FullName) == NuGet.Constants.ManifestExtension);

                using (var manifestStream = manifestFile.Open())
                {
                    var manifest = Manifest.ReadFrom(manifestStream, validateSchema: false);
                    manifest.Metadata.Version = version;

                    manifestStream.SetLength(0);
                    manifest.Save(manifestStream);
                }
            }

            packageStream.Seek(0, SeekOrigin.Begin);
            var outputPackage = new ZipPackage(packageStream);

            return outputPackage;
        }
    }
}
