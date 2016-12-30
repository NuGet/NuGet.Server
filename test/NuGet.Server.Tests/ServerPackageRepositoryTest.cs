// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Versioning;
using Moq;
using NuGet.Server.Infrastructure;
using NuGet.Server.Tests.Infrastructure;
using Xunit;

namespace NuGet.Server.Tests
{
    public class ServerPackageRepositoryTest
    {
        public ServerPackageRepository CreateServerPackageRepository(
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
                logger: new Logging.NullLogger(),
                getSetting: getSetting);

            serverRepository.GetPackages(); // caches the files

            return serverRepository;
        }

        private ServerPackageRepository CreateServerPackageRepositoryWithSemVer2(TemporaryDirectory temporaryDirectory)
        {
            return CreateServerPackageRepository(temporaryDirectory.Path, repository =>
            {
                repository.AddPackage(CreatePackage("test1", "1.0"));
                repository.AddPackage(CreatePackage("test2", "1.0-beta"));
                repository.AddPackage(CreatePackage("test3", "1.0-beta.1"));
                repository.AddPackage(CreatePackage("test4", "1.0-beta+foo"));
            });
        }

        [Fact]
        public void ServerPackageRepositoryAddsPackagesFromDropFolderOnStart()
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

                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path);

                // Act
                var packages = serverRepository.GetPackages(ClientCompatibility.Max);

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
        public void ServerPackageRepositoryRemovePackage()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path, repository =>
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
                serverRepository.RemovePackage(CreateMockPackage("test", "1.11"));
                serverRepository.RemovePackage(CreateMockPackage("test", "2.0-alpha"));
                serverRepository.RemovePackage(CreateMockPackage("test", "2.0.1"));
                serverRepository.RemovePackage(CreateMockPackage("test", "2.0.0-0test"));
                var packages = serverRepository.GetPackages(ClientCompatibility.Max);

                // Assert
                Assert.Equal(3, packages.Count());
                Assert.Equal(1, packages.Count(p => p.SemVer2IsLatest));
                Assert.Equal("2.0.0", packages.First(p => p.SemVer2IsLatest).Version.ToString());

                Assert.Equal(1, packages.Count(p => p.SemVer2IsAbsoluteLatest));
                Assert.Equal("2.0.0", packages.First(p => p.SemVer2IsAbsoluteLatest).Version.ToString());
            }
        }

        [Fact]
        public void ServerPackageRepositorySearch()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path, repository =>
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
                var includePrerelease = serverRepository.Search(
                    "test3",
                    targetFrameworks: Enumerable.Empty<string>(),
                    allowPrereleaseVersions: true,
                    compatibility: ClientCompatibility.Max);
                var excludePrerelease = serverRepository.Search(
                    "test3",
                    targetFrameworks: Enumerable.Empty<string>(),
                    allowPrereleaseVersions: false,
                    compatibility: ClientCompatibility.Max);
                var ignoreTag = serverRepository.Search(
                    "test6",
                    targetFrameworks: Enumerable.Empty<string>(),
                    allowPrereleaseVersions: false,
                    compatibility: ClientCompatibility.Max);

                // Assert
                Assert.Equal("test3", includePrerelease.First().Id);
                Assert.Equal(2, includePrerelease.Count());
                Assert.Equal(1, excludePrerelease.Count());
                Assert.Equal("test6", ignoreTag.First().Id);
                Assert.Equal(1, ignoreTag.Count());
            }
        }

        [Fact]
        public void ServerPackageRepositorySearchSupportsFilteringOutSemVer2()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepositoryWithSemVer2(temporaryDirectory);

                // Act
                var actual = serverRepository.Search(
                    "test",
                    targetFrameworks: Enumerable.Empty<string>(),
                    allowPrereleaseVersions: true,
                    compatibility: ClientCompatibility.Default);

                // Assert
                var packages = actual.OrderBy(p => p.Id).ToList();
                Assert.Equal(2, packages.Count);
                Assert.Equal("test1", packages[0].Id);
                Assert.Equal("test2", packages[1].Id);
            }
        }

        [Fact]
        public void ServerPackageRepositorySearchUnlisted()
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

                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test1", "1.0"));
                }, getSetting);

                // Assert base setup
                var packages = serverRepository.Search("test1", true).ToList();
                Assert.Equal(1, packages.Count);
                Assert.Equal("test1", packages[0].Id);
                Assert.Equal("1.0", packages[0].Version.ToString());

                // Delist the package
                serverRepository.RemovePackage("test1", new SemanticVersion("1.0"));

                // Verify that the package is not returned by search
                packages = serverRepository.Search("test1", allowPrereleaseVersions: true).ToList();
                Assert.Equal(0, packages.Count);

                // Act: search with includeDelisted=true
                packages = serverRepository.GetPackages().ToList();

                // Assert
                Assert.Equal(1, packages.Count);
                Assert.Equal("test1", packages[0].Id);
                Assert.Equal("1.0", packages[0].Version.ToString());
                Assert.False(packages[0].Listed);
            }
        }

        [Fact]
        public void ServerPackageRepositoryFindPackageById()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "1.0"));
                    repository.AddPackage(CreatePackage("test2", "1.0"));
                    repository.AddPackage(CreatePackage("test3", "1.0-alpha"));
                    repository.AddPackage(CreatePackage("test4", "2.0"));
                    repository.AddPackage(CreatePackage("test4", "3.0.0+tagged"));
                    repository.AddPackage(CreatePackage("Not5", "4.0"));
                });

                // Act
                var valid = serverRepository.FindPackagesById("test");
                var invalid = serverRepository.FindPackagesById("bad");

                // Assert
                Assert.Equal("test", valid.First().Id);
                Assert.Equal(0, invalid.Count());
            }
        }

        [Fact]
        public void ServerPackageRepositoryFindPackageByIdSupportsFilteringOutSemVer2()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepositoryWithSemVer2(temporaryDirectory);

                // Act
                var actual = serverRepository.FindPackagesById("test3");

                // Assert
                Assert.Empty(actual);
            }
        }

        [Fact]
        public void ServerPackageRepositoryFindPackage()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "1.0"));
                    repository.AddPackage(CreatePackage("test2", "1.0"));
                    repository.AddPackage(CreatePackage("test3", "1.0.0-alpha"));
                    repository.AddPackage(CreatePackage("test4", "2.0"));
                    repository.AddPackage(CreatePackage("test4", "3.0.0+tagged"));
                    repository.AddPackage(CreatePackage("Not5", "4.0.0"));
                });

                // Act
                var valid = serverRepository.FindPackage("test4", new SemanticVersion("3.0.0"));
                var valid2 = serverRepository.FindPackage("Not5", new SemanticVersion("4.0"));
                var validPreRel = serverRepository.FindPackage("test3", new SemanticVersion("1.0.0-alpha"));
                var invalidPreRel = serverRepository.FindPackage("test3", new SemanticVersion("1.0.0"));
                var invalid = serverRepository.FindPackage("bad", new SemanticVersion("1.0"));

                // Assert
                Assert.Equal("test4", valid.Id);
                Assert.Equal("Not5", valid2.Id);
                Assert.Equal("test3", validPreRel.Id);
                Assert.Null(invalidPreRel);
                Assert.Null(invalid);
            }
        }

        [Fact]
        public void ServerPackageRepositoryMultipleIds()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path, repository =>
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
                var packages = serverRepository.GetPackages(ClientCompatibility.Max);

                // Assert
                Assert.Equal(5, packages.Count(p => p.SemVer2IsAbsoluteLatest));
                Assert.Equal(4, packages.Count(p => p.SemVer2IsLatest));
                Assert.Equal(3, packages.Count(p => !p.SemVer2IsAbsoluteLatest));
                Assert.Equal(4, packages.Count(p => !p.SemVer2IsLatest));
            }
        }

        [Fact]
        public void ServerPackageRepositorySemVer1IsAbsoluteLatest()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "2.0-alpha"));
                    repository.AddPackage(CreatePackage("test", "2.1-alpha"));
                    repository.AddPackage(CreatePackage("test", "2.2-beta"));
                    repository.AddPackage(CreatePackage("test", "2.3"));
                    repository.AddPackage(CreatePackage("test", "2.4.0-prerel"));
                    repository.AddPackage(CreatePackage("test", "3.2.0+taggedOnly"));
                });

                // Act
                var packages = serverRepository.GetPackages(ClientCompatibility.Default);

                // Assert
                Assert.Equal(1, packages.Count(p => p.SemVer1IsAbsoluteLatest));
                Assert.Equal("2.4.0-prerel", packages.First(p => p.SemVer1IsAbsoluteLatest).Version.ToString());
            }
        }

        [Fact]
        public void ServerPackageRepositorySemVer2IsAbsoluteLatest()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "2.0-alpha"));
                    repository.AddPackage(CreatePackage("test", "2.1-alpha"));
                    repository.AddPackage(CreatePackage("test", "2.2-beta"));
                    repository.AddPackage(CreatePackage("test", "2.3"));
                    repository.AddPackage(CreatePackage("test", "2.4.0-prerel"));
                    repository.AddPackage(CreatePackage("test", "3.2.0+taggedOnly"));
                });

                // Act
                var packages = serverRepository.GetPackages(ClientCompatibility.Max);

                // Assert
                Assert.Equal(1, packages.Count(p => p.SemVer2IsAbsoluteLatest));
                Assert.Equal("3.2.0", packages.First(p => p.SemVer2IsAbsoluteLatest).Version.ToString());
            }
        }

        [Fact]
        public void ServerPackageRepositorySemVer2IsLatestOnlyPreRel()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "2.0-alpha"));
                    repository.AddPackage(CreatePackage("test", "2.1-alpha"));
                    repository.AddPackage(CreatePackage("test", "2.2-beta+tagged"));
                });
                
                // Act
                var packages = serverRepository.GetPackages(ClientCompatibility.Max);

                // Assert
                Assert.Equal(0, packages.Count(p => p.SemVer2IsLatest));
            }
        }

        [Fact]
        public void ServerPackageRepositorySemVer1IsLatest()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test1", "1.0.0"));
                    repository.AddPackage(CreatePackage("test1", "1.2.0+taggedOnly"));
                    repository.AddPackage(CreatePackage("test1", "2.0.0-alpha"));
                });

                // Act
                var packages = serverRepository.GetPackages(ClientCompatibility.Default);

                // Assert
                Assert.Equal(1, packages.Count(p => p.SemVer1IsLatest));
                Assert.Equal("1.0.0", packages.First(p => p.SemVer1IsLatest).Version.ToString());
            }
        }

        [Fact]
        public void ServerPackageRepositorySemVer2IsLatest()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("test", "1.11"));
                    repository.AddPackage(CreatePackage("test", "1.9"));
                    repository.AddPackage(CreatePackage("test", "2.0-alpha"));
                    repository.AddPackage(CreatePackage("test1", "1.0.0"));
                    repository.AddPackage(CreatePackage("test1", "1.2.0+taggedOnly"));
                    repository.AddPackage(CreatePackage("test1", "2.0.0-alpha"));
                });

                // Act
                var packages = serverRepository.GetPackages(ClientCompatibility.Max);

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
        public void ServerPackageRepositoryReadsDerivedData()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var package = CreatePackage("test", "1.0");
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(package);
                });

                // Act
                var packages = serverRepository.GetPackages();
                var singlePackage = packages.Single() as ServerPackage;

                // Assert
                Assert.NotNull(singlePackage);
                Assert.Equal(package.GetStream().Length, singlePackage.PackageSize);
            }
        }

        [Fact]
        public void ServerPackageRepositoryEmptyRepo()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                CreatePackage("test", "1.0");
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path);

                // Act
                var findPackage = serverRepository.FindPackage("test", new SemanticVersion("1.0"));
                var findPackagesById = serverRepository.FindPackagesById("test");
                var getPackages = serverRepository.GetPackages().ToList();
                var getPackagesWithDerivedData = serverRepository.GetPackages().ToList();
                var getUpdates = serverRepository.GetUpdates(Enumerable.Empty<IPackageName>(), true, true, Enumerable.Empty<FrameworkName>(), Enumerable.Empty<IVersionSpec>());
                var search = serverRepository.Search("test", true).ToList();
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
        public void ServerPackageRepositoryAddPackage()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path);

                // Act
                serverRepository.AddPackage(CreatePackage("Foo", "1.0.0"));

                // Assert
                Assert.True(serverRepository.Exists("Foo", new SemanticVersion("1.0.0")));
            }
        }

        [Fact]
        public void ServerPackageRepositoryAddPackageSemVer2()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path);

                // Act
                serverRepository.AddPackage(CreatePackage("Foo", "1.0.0+foo"));

                // Assert
                Assert.True(serverRepository.Exists("Foo", new SemanticVersion("1.0.0")));
            }
        }

        [Fact]
        public void ServerPackageRepositoryRemovePackageSemVer2()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(temporaryDirectory.Path);
                serverRepository.AddPackage(CreatePackage("Foo", "1.0.0+foo"));

                // Act
                serverRepository.RemovePackage("Foo", new SemanticVersion("1.0.0+bar"));

                // Assert
                Assert.False(serverRepository.Exists("Foo", new SemanticVersion("1.0.0")));
            }
        }

        [Fact]
        public void ServerPackageRepositoryAddPackageRejectsDuplicatesWithSemVer2()
        {
            using (var temporaryDirectory = new TemporaryDirectory())
            {
                // Arrange
                var serverRepository = CreateServerPackageRepository(
                    temporaryDirectory.Path,
                    getSetting: (key, defaultValue) =>
                    {
                        if (key == "allowOverrideExistingPackageOnPush")
                        {
                            return false;
                        }

                        return defaultValue;
                    });
                serverRepository.AddPackage(CreatePackage("Foo", "1.0.0-beta.1+foo"));

                // Act & Assert
                var actual = Assert.Throws<InvalidOperationException>(() =>
                    serverRepository.AddPackage(CreatePackage("Foo", "1.0.0-beta.1+bar")));
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
            package.Setup(p => p.Listed).Returns(true);

            return package.Object;
        }

        private IPackage CreatePackage(string id, string version)
        {
            var parsedVersion = new SemanticVersion(version);
            var packageBuilder = new PackageBuilder
            {
                Id = id,
                Version = parsedVersion,
                Description = "Description",
                Authors = { "Test Author" }
            };

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
