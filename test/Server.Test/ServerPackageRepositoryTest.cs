using Moq;
using NuGet.Server.Infrastructure;
using NuGet.Test.Mocks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Xunit;
using Xunit.Extensions;

namespace NuGet.Test.Server.Infrastructure
{
    public class ServerPackageRepositoryTest
    {
        private const string EnablePersistNupkgHash = "enablePersistNupkgHash";

        [Fact]
        public void ServerPackageRepositoryRemovePackage()
        {
            // Arrange
            var mockProjectSystem = new Mock<MockProjectSystem>() { CallBase = true };

            AddPackage(mockProjectSystem, "test", "1.11");
            AddPackage(mockProjectSystem, "test", "1.9");
            AddPackage(mockProjectSystem, "test", "2.0-alpha");

            var serverRepository = CreateServerPackageRepository(mockProjectSystem);

            var package = CreatePackage("test", "1.11");
            var package2 = CreatePackage("test", "2.0-alpha");

            // call to cache the first time
            var packages = serverRepository.GetPackagesWithDerivedData();

            // Act
            serverRepository.RemovePackage(package);
            serverRepository.RemovePackage(package2);
            packages = serverRepository.GetPackagesWithDerivedData();

            // Assert
            Assert.Equal(1, packages.Count());
            Assert.Equal(1, packages.Where(p => p.IsLatestVersion).Count());
            Assert.Equal("1.9", packages.Where(p => p.IsLatestVersion).First().Version);

            Assert.Equal(1, packages.Where(p => p.IsAbsoluteLatestVersion).Count());
            Assert.Equal("1.9", packages.Where(p => p.IsAbsoluteLatestVersion).First().Version);
        }

        [Fact]
        public void ServerPackageRepositorySearch()
        {
            // Arrange
            var mockProjectSystem = new Mock<MockProjectSystem>() { CallBase = true };

            AddPackage(mockProjectSystem, "test", "1.0");
            AddPackage(mockProjectSystem, "test2", "1.0");
            AddPackage(mockProjectSystem, "test3", "1.0-alpha");
            AddPackage(mockProjectSystem, "test4", "2.0");

            var serverRepository = CreateServerPackageRepository(mockProjectSystem);

            // Act
            var valid = serverRepository.Search("test3", true);
            var invalid = serverRepository.Search("test3", false);

            // Assert
            Assert.Equal("test3", valid.First().Id);
            Assert.Equal(0, invalid.Count());
        }

        [Fact]
        public void ServerPackageRepositorySearchUnlisted()
        {   
            var tempPath = Path.GetTempPath();
            var workingDirectory = Path.Combine(tempPath, Guid.NewGuid().ToString());

            try
            {
                // Arrange
                // create the server repo, from a directory containing a package.
                Func<string, bool, bool> settingsFunc = (key, defaultValue) =>
                {
                    if (key == "enableDelisting")
                    {
                        return true;
                    }
                    return defaultValue;
                };

                CreateDirectory(workingDirectory);
                var packageFile = CreatePackage("test1", "1.0", workingDirectory);

                var fileSystem = new PhysicalFileSystem(workingDirectory);
                var serverRepository = new ServerPackageRepository(
                    new DefaultPackagePathResolver(fileSystem),
                    fileSystem,
                    settingsFunc)
                {
                    HashProvider = GetHashProvider().Object
                };

                var packages = serverRepository.Search("test1", true).ToList();
                Assert.Equal(1, packages.Count);
                Assert.Equal("test1", packages[0].Id);
                Assert.Equal("1.0", packages[0].Version.ToString());

                // delist the package
                serverRepository.RemovePackage("test1", new SemanticVersion("1.0"));

                // verify that the package is not returned by search
                packages = serverRepository.Search("test1", true).ToList();
                Assert.Equal(0, packages.Count);

                // Act: search with includeDelisted=true
                packages = serverRepository.Search(
                    "test1", 
                    targetFrameworks: Enumerable.Empty<string>(),
                    allowPrereleaseVersions: true).ToList();

                // Verify
                Assert.Equal(1, packages.Count);
                Assert.Equal("test1", packages[0].Id);
                Assert.Equal("1.0", packages[0].Version.ToString());
            }
            finally
            {
                // Cleanup
                DeleteDirectory(workingDirectory);
            }
        }


        [Fact]
        public void ServerPackageRepositoryFindPackageById()
        {
            // Arrange
            var mockProjectSystem = new Mock<MockProjectSystem>() { CallBase = true };

            AddPackage(mockProjectSystem, "test", "1.0");
            AddPackage(mockProjectSystem, "test2", "1.0");
            AddPackage(mockProjectSystem, "test3", "1.0-alpha");
            AddPackage(mockProjectSystem, "test4", "2.0");

            var serverRepository = CreateServerPackageRepository(mockProjectSystem);

            // Act
            var valid = serverRepository.FindPackagesById("test");
            var invalid = serverRepository.FindPackagesById("bad");

            // Assert
            Assert.Equal("test", valid.First().Id);
            Assert.Equal(0, invalid.Count());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ServerPackageRepositoryPersistHashTest(bool enablePersistNupkgHash)
        {
            const int NbInvalidate = 3;
            const int NbPackages = 2;

            var settings = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
               { EnablePersistNupkgHash, enablePersistNupkgHash }
            };

            // Arrange.
            var mockProjectSystem = new Mock<MockProjectSystem>() { CallBase = true };
            var hashProvider = GetHashProvider();

            AddPackage(mockProjectSystem, "test", "1.11");
            AddPackage(mockProjectSystem, "test", "1.3");

            var serverRepository = CreateServerPackageRepository(mockProjectSystem, hashProvider.Object, settings);
            // First call.
            var packagesFirstCall = serverRepository.GetPackagesWithDerivedData().OrderBy(p => p.Id + "." + p.Version).ToList();
            Assert.Equal(NbPackages, packagesFirstCall.Count);

            // Subsequent calls where we invalidate the cache.
            for (var j = 0; j < NbInvalidate; j++)
            {
                // Invalidate cache.
                serverRepository.InvalidatePackages();

                var packagesSubsequentCall = serverRepository.GetPackagesWithDerivedData().OrderBy(p => p.Id + "." + p.Version).ToList();
                Assert.Equal(NbPackages, packagesSubsequentCall.Count);

                for (var i = 0; i < NbPackages; i++)
                {
                    // Verify that we're getting the same values for hash and size after invalidating cache (both lists are sorted).
                    Assert.Equal(packagesFirstCall[i].PackageHash, packagesSubsequentCall[i].PackageHash);
                    Assert.Equal(packagesFirstCall[i].PackageSize, packagesSubsequentCall[i].PackageSize);
                }

                // Verify that when and only when hash persisting is turned on, the hash is preserved to disk, 
                // ensuring the information is preserved when process is recycled.
                var hashFiles = mockProjectSystem.Object.GetFiles(string.Empty, "*hash", true);
                Assert.Equal(enablePersistNupkgHash ? NbPackages : 0, hashFiles.Count());
            }

            // Verify that hashes are always (re)computed when enablePersistNupkgHash is turned off, and at most once per package otherwise.
            // also verify that Streams are used instead of byte arrays (prone to OOM) to compute hashes.
            var expectedHashProviderCallCount = enablePersistNupkgHash ? NbPackages : NbPackages * (NbInvalidate + 1);
            hashProvider.Verify(h => h.CalculateHash(It.IsAny<Stream>()), Times.Exactly(expectedHashProviderCallCount));
            hashProvider.Verify(h => h.CalculateHash(It.IsAny<byte[]>()), Times.Never);
        }

        [Fact]
        public void ServerPackageRepositoryFindPackage()
        {
            // Arrange
            var mockProjectSystem = new Mock<MockProjectSystem>() { CallBase = true };

            AddPackage(mockProjectSystem, "test", "1.0");
            AddPackage(mockProjectSystem, "test2", "1.0");
            AddPackage(mockProjectSystem, "test3", "1.0-alpha");
            AddPackage(mockProjectSystem, "test4", "2.0");

            var serverRepository = CreateServerPackageRepository(mockProjectSystem);

            // Act
            var valid = serverRepository.FindPackage("test", new SemanticVersion("1.0"));
            var invalid = serverRepository.FindPackage("bad", new SemanticVersion("1.0"));

            // Assert
            Assert.Equal("test", valid.Id);
            Assert.Null(invalid);
        }

        [Fact]
        public void ServerPackageRepositoryMultipleIds()
        {
            // Arrange
            var mockProjectSystem = new Mock<MockProjectSystem>() { CallBase = true };

            AddPackage(mockProjectSystem, "test", "0.9");
            AddPackage(mockProjectSystem, "test", "1.0");
            AddPackage(mockProjectSystem, "test2", "1.0");
            AddPackage(mockProjectSystem, "test3", "1.0-alpha");
            AddPackage(mockProjectSystem, "test4", "2.0");

            var serverRepository = CreateServerPackageRepository(mockProjectSystem);

            // Act
            var packages = serverRepository.GetPackagesWithDerivedData();

            // Assert
            Assert.Equal(4, packages.Where(p => p.IsAbsoluteLatestVersion).Count());
            Assert.Equal(3, packages.Where(p => p.IsLatestVersion).Count());
            Assert.Equal(1, packages.Where(p => !p.IsAbsoluteLatestVersion).Count());
            Assert.Equal(2, packages.Where(p => !p.IsLatestVersion).Count());
        }

        [Fact]
        public void ServerPackageRepositoryIsAbsoluteLatest()
        {
            // Arrange
            var mockProjectSystem = new Mock<MockProjectSystem>() { CallBase = true };

            AddPackage(mockProjectSystem, "test", "2.0-alpha");
            AddPackage(mockProjectSystem, "test", "2.1-alpha");
            AddPackage(mockProjectSystem, "test", "2.2-beta");
            AddPackage(mockProjectSystem, "test", "2.3");

            var serverRepository = CreateServerPackageRepository(mockProjectSystem);

            // Act
            var packages = serverRepository.GetPackagesWithDerivedData();

            // Assert
            Assert.Equal(1, packages.Where(p => p.IsAbsoluteLatestVersion).Count());
            Assert.Equal("2.3", packages.Where(p => p.IsAbsoluteLatestVersion).First().Version);
        }

        [Fact]
        public void ServerPackageRepositoryIsLatestOnlyPreRel()
        {
            // Arrange
            var mockProjectSystem = new Mock<MockProjectSystem>() { CallBase = true };

            AddPackage(mockProjectSystem, "test", "2.0-alpha");
            AddPackage(mockProjectSystem, "test", "2.1-alpha");
            AddPackage(mockProjectSystem, "test", "2.2-beta");

            var serverRepository = CreateServerPackageRepository(mockProjectSystem);

            // Act
            var packages = serverRepository.GetPackagesWithDerivedData();

            // Assert
            Assert.Equal(0, packages.Where(p => p.IsLatestVersion).Count());
        }

        [Fact]
        public void ServerPackageRepositoryIsLatest()
        {
            // Arrange
            var mockProjectSystem = new Mock<MockProjectSystem>() { CallBase = true };

            AddPackage(mockProjectSystem, "test", "1.11");
            AddPackage(mockProjectSystem, "test", "1.9");
            AddPackage(mockProjectSystem, "test", "2.0-alpha");

            var serverRepository = CreateServerPackageRepository(mockProjectSystem);

            // Act
            var packages = serverRepository.GetPackagesWithDerivedData();

            // Assert
            Assert.Equal(1, packages.Where(p => p.IsLatestVersion).Count());
            Assert.Equal("1.11", packages.Where(p => p.IsLatestVersion).First().Version);
        }

        [Fact]
        public void ServerPackageRepositoryReadsDerivedData()
        {
            // Arrange
            var mockProjectSystem = new Mock<MockProjectSystem>() { CallBase = true };
            var package = new PackageBuilder() { Id = "Test", Version = new SemanticVersion("1.0"), Description = "Description" };
            var mockFile = new Mock<IPackageFile>();
            mockFile.Setup(m => m.Path).Returns("foo");
            mockFile.Setup(m => m.GetStream()).Returns(new MemoryStream());
            package.Files.Add(mockFile.Object);
            package.Authors.Add("Test Author");
            var memoryStream = new MemoryStream();
            package.Save(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);
            mockProjectSystem.Object.AddFile("foo.nupkg");
            mockProjectSystem.Setup(c => c.OpenFile(It.IsAny<string>())).Returns(() => new MemoryStream(memoryStream.ToArray()));
            var serverRepository = CreateServerPackageRepository(mockProjectSystem);

            // Act
            var packages = serverRepository.GetPackagesWithDerivedData();

            // Assert
            byte[] data = memoryStream.ToArray();
            Assert.Equal(data.Length, packages.Single().PackageSize);
            Assert.Equal(data.Select(Invert).ToArray(), Convert.FromBase64String(packages.Single().PackageHash).ToArray());

            //CollectionAssert.AreEquivalent(data.Select(Invert).ToArray(), Convert.FromBase64String(packages.Single().PackageHash));
            Assert.Equal(data.Length, packages.Single().PackageSize);
        }

        [Fact]
        public void ServerPackageRepositoryEmptyRepo()
        {
            // Arrange
            var mockProjectSystem = new Mock<MockProjectSystem>() { CallBase = true };

            var serverRepository = CreateServerPackageRepository(mockProjectSystem);

            var package = CreatePackage("test", "1.0");

            // Act
            var findPackage = serverRepository.FindPackage("test", new SemanticVersion("1.0"));
            var findPackagesById = serverRepository.FindPackagesById("test");
            var getMetadataPackage = serverRepository.GetMetadataPackage(package);
            var getPackages = serverRepository.GetPackages().ToList();
            var getPackagesWithDerivedData = serverRepository.GetPackagesWithDerivedData().ToList();
            var getUpdates = serverRepository.GetUpdates(Enumerable.Empty<IPackageName>(), true, true, Enumerable.Empty<FrameworkName>(), Enumerable.Empty<IVersionSpec>());
            var search = serverRepository.Search("test", true).ToList();
            var source = serverRepository.Source;

            // Assert
            Assert.Null(findPackage);
            Assert.Empty(findPackagesById);
            Assert.Null(getMetadataPackage);
            Assert.Empty(getPackages);
            Assert.Null(getMetadataPackage);
            Assert.Empty(getPackagesWithDerivedData);
            Assert.Empty(getUpdates);
            Assert.Empty(search);
            Assert.NotEmpty(source);
        }

        private static ServerPackageRepository CreateServerPackageRepository(Mock<MockProjectSystem> mockProjectSystem, IHashProvider hashProvider = null, IDictionary<string, bool> settings = null)
        {
            Func<string, bool, bool> settingsFunc = null;
            if (settings != null)
            {
                settingsFunc = (key, defaultValue) =>
                {
                    bool ret;
                    return settings.TryGetValue(key, out ret) ? ret : defaultValue;
                };
            }
            return new ServerPackageRepository(new DefaultPackagePathResolver(mockProjectSystem.Object), mockProjectSystem.Object, settingsFunc)
            {
                HashProvider = hashProvider ?? GetHashProvider().Object
            };
        }

        private static IPackage CreatePackage(string id, string version)
        {
            var package = new Mock<IPackage>();
            package.Setup(p => p.Id).Returns(id);
            package.Setup(p => p.Version).Returns(new SemanticVersion(version));
            package.Setup(p => p.IsLatestVersion).Returns(true);
            package.Setup(p => p.Listed).Returns(true);

            return package.Object;
        }

        private void AddPackage(Mock<MockProjectSystem> mockProjectSystem, string id, string version)
        {
            string name = String.Format(CultureInfo.InvariantCulture, "{0}.{1}.nupkg", id, version);

            var package = new PackageBuilder() { Id = id, Version = new SemanticVersion(version), Description = "Description" };
            var mockFile = new Mock<IPackageFile>();
            mockFile.Setup(m => m.Path).Returns(name);
            mockFile.Setup(m => m.GetStream()).Returns(new MemoryStream());
            package.Files.Add(mockFile.Object);
            package.Authors.Add("Test Author");
            var memoryStream = new MemoryStream();
            package.Save(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);
            mockProjectSystem.Object.AddFile(name, memoryStream);
        }

        private static Mock<IHashProvider> GetHashProvider()
        {
            var hashProvider = new Mock<IHashProvider>();
            hashProvider.Setup(c => c.CalculateHash(It.IsAny<byte[]>())).Returns((byte[] value) => value.Select(Invert).ToArray());
            hashProvider.Setup(c => c.CalculateHash(It.IsAny<Stream>())).Returns((Stream value) => value.ReadAllBytes().Select(Invert).ToArray());

            return hashProvider;
        }

        private static byte Invert(byte value)
        {
            return (byte)~value;
        }

        public static string CreatePackage(string id, string version, string outputDirectory,
            Action<PackageBuilder> additionalAction = null)
        {
            PackageBuilder builder = new PackageBuilder()
            {
                Id = id,
                Version = new SemanticVersion(version),
                Description = "Descriptions",
            };
            builder.Authors.Add("test");
            builder.Files.Add(CreatePackageFile(@"content\test1.txt"));
            if (additionalAction != null)
            {
                additionalAction(builder);
            }

            var packageFileName = Path.Combine(outputDirectory, id + "." + version + ".nupkg");
            using (var stream = new FileStream(packageFileName, FileMode.CreateNew))
            {
                builder.Save(stream);
            }

            return packageFileName;
        }

        private static IPackageFile CreatePackageFile(string name)
        {
            var file = new Mock<IPackageFile>();
            file.SetupGet(f => f.Path).Returns(name);
            file.Setup(f => f.GetStream()).Returns(new MemoryStream());

            string effectivePath;
            var fx = VersionUtility.ParseFrameworkNameFromFilePath(name, out effectivePath);
            file.SetupGet(f => f.EffectivePath).Returns(effectivePath);
            file.SetupGet(f => f.TargetFramework).Returns(fx);

            return file.Object;
        }

        /// <summary>
        /// Creates the specified directory. If it exists, it's first deleted before 
        /// it's created. Thus, the directory is guaranteed to be empty.
        /// </summary>
        /// <param name="directory">The directory to be created.</param>
        private static void CreateDirectory(string directory)
        {
            DeleteDirectory(directory);
            Directory.CreateDirectory(directory);
        }

        /// <summary>
        /// Deletes the specified directory.
        /// </summary>
        /// <param name="packageDirectory">The directory to be deleted.</param>
        private static void DeleteDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
