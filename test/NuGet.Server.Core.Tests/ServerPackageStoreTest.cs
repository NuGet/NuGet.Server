// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.Core.Tests.Infrastructure;
using Xunit;

namespace NuGet.Server.Core.Tests
{
    public class ServerPackageStoreTest
    {
        private static CancellationToken Token => CancellationToken.None;
        private const string PackageId = "NuGet.Versioning";
        private const string PackageVersionString = "3.5.0";
        private static readonly SemanticVersion PackageVersion = new SemanticVersion(PackageVersionString);

        [Fact]
        public async Task Remove_SupportsEnabledUnlisting()
        {
            // Arrange
            using (var directory = new TemporaryDirectory())
            {
                var fileSystem = new PhysicalFileSystem(directory);
                var repository = new ExpandedPackageRepository(fileSystem);
                var logger = new Infrastructure.NullLogger();

                repository.AddPackage(CreatePackage(PackageId, PackageVersion));

                var target = new ServerPackageStore(fileSystem, repository, logger);

                // Act
                target.Remove(PackageId, PackageVersion, enableDelisting: true);

                // Assert
                var package = (await target.GetAllAsync(enableDelisting: true, token: Token)).SingleOrDefault();
                Assert.NotNull(package);
                Assert.Equal(PackageId, package.Id);
                Assert.Equal(PackageVersion, package.Version);
                Assert.False(package.Listed);

                var fileInfo = new FileInfo(package.FullPath);
                Assert.True(fileInfo.Exists);
                Assert.Equal(FileAttributes.Hidden, fileInfo.Attributes & FileAttributes.Hidden);
            }
        }

        [Fact]
        public async Task Remove_SupportsDisabledUnlisting()
        {
            // Arrange
            using (var directory = new TemporaryDirectory())
            {
                var fileSystem = new PhysicalFileSystem(directory);
                var repository = new ExpandedPackageRepository(fileSystem);
                var logger = new Infrastructure.NullLogger();

                repository.AddPackage(CreatePackage(PackageId, PackageVersion));

                var target = new ServerPackageStore(fileSystem, repository, logger);

                // Act
                target.Remove(PackageId, PackageVersion, enableDelisting: false);

                // Assert
                Assert.Empty(await target.GetAllAsync(enableDelisting: false, token: Token));
                Assert.Empty(repository.GetPackages());
            }
        }

        [Fact]
        public async Task Remove_NoOpsWhenPackageDoesNotExist()
        {
            // Arrange
            using (var directory = new TemporaryDirectory())
            {
                var fileSystem = new PhysicalFileSystem(directory);
                var repository = new ExpandedPackageRepository(fileSystem);
                var logger = new Infrastructure.NullLogger();

                repository.AddPackage(CreatePackage(PackageId, PackageVersion));

                var target = new ServerPackageStore(fileSystem, repository, logger);

                // Act
                target.Remove("Foo", PackageVersion, enableDelisting: false);

                // Assert
                var package = (await target.GetAllAsync(enableDelisting: false, token: Token)).FirstOrDefault();
                Assert.NotNull(package);
                Assert.Equal(PackageId, package.Id);
                Assert.Equal(PackageVersion, package.Version);
                Assert.True(package.Listed);
            }
        }

        private IPackage CreatePackage(string id, SemanticVersion version)
        {
            var file = new Mock<IPackageFile>();
            file.Setup(x => x.GetStream()).Returns(() => Stream.Null);
            file.Setup(x => x.Path).Returns($"lib/net40/test.dll");

            var builder = new PackageBuilder
            {
                Id = id,
                Version = version,
                Description = id,
                Authors = { id },
                Files = { file.Object }
            };

            var memoryStream = new MemoryStream();
            builder.Save(memoryStream);
            memoryStream.Position = 0;

            return new ZipPackage(memoryStream);
        }
    }
}
