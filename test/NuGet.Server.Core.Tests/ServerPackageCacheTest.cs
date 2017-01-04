// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.IO;
using System.Linq;
using System.Text;
using Moq;
using NuGet.Server.Core.Infrastructure;
using Xunit;

namespace NuGet.Server.Core.Tests
{
    public class ServerPackageCacheTest
    {
        private const string CacheFileName = "store.json";

        private const string PackageId = "NuGet.Versioning";
        private const string PackageVersionString = "3.5.0";
        private static readonly SemanticVersion PackageVersion = new SemanticVersion(PackageVersionString);
        private const string MinimalCacheFile =
            "[{\"Id\":\"" + PackageId + "\",\"Version\":\"" + PackageVersionString + "\",\"Listed\":true}]";

        [Theory]
        [InlineData("[")]
        [InlineData("]")]
        [InlineData("{")]
        [InlineData("}")]
        [InlineData("[{")]
        [InlineData("[{}")]
        [InlineData("{}")]
        [InlineData("[{\"foo\": \"bar\"}]")]
        public void Constructor_IgnoresAndDeletesInvalidCacheFile(string content)
        {
            // Arrange
            var fileSystem = new Mock<IFileSystem>();
            fileSystem
                .Setup(x => x.FileExists(CacheFileName))
                .Returns(true);
            fileSystem
                .Setup(x => x.OpenFile(CacheFileName))
                .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(content)));

            // Act
            var actual = new ServerPackageCache(fileSystem.Object, CacheFileName);

            // Assert
            Assert.Empty(actual.GetAll());
            fileSystem.Verify(x => x.DeleteFile(CacheFileName), Times.Once);
        }

        [Theory]
        [InlineData("", 0)]
        [InlineData("[]", 0)]
        [InlineData(MinimalCacheFile, 1)]
        public void Constructor_LeavesValidCacheFile(string content, int count)
        {
            // Arrange
            var fileSystem = new Mock<IFileSystem>();
            fileSystem
                .Setup(x => x.FileExists(CacheFileName))
                .Returns(true);
            fileSystem
                .Setup(x => x.OpenFile(CacheFileName))
                .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(content)));

            // Act
            var actual = new ServerPackageCache(fileSystem.Object, CacheFileName);

            // Assert
            Assert.Equal(count, actual.GetAll().Count());
            fileSystem.Verify(x => x.DeleteFile(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void Remove_SupportsEnabledUnlisting()
        {
            // Arrange
            var fileSystem = new Mock<IFileSystem>();
            fileSystem
                .Setup(x => x.FileExists(CacheFileName))
                .Returns(true);
            fileSystem
                .Setup(x => x.OpenFile(CacheFileName))
                .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(MinimalCacheFile)));
            var target = new ServerPackageCache(fileSystem.Object, CacheFileName);

            // Act
            target.Remove(PackageId, PackageVersion, enableDelisting: true);

            // Assert
            var package = target.GetAll().FirstOrDefault();
            Assert.NotNull(package);
            Assert.Equal(PackageId, package.Id);
            Assert.Equal(PackageVersion, package.Version);
            Assert.False(package.Listed);
        }

        [Fact]
        public void Remove_SupportsDisabledUnlisting()
        {
            // Arrange
            var fileSystem = new Mock<IFileSystem>();
            fileSystem
                .Setup(x => x.FileExists(CacheFileName))
                .Returns(true);
            fileSystem
                .Setup(x => x.OpenFile(CacheFileName))
                .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(MinimalCacheFile)));
            var target = new ServerPackageCache(fileSystem.Object, CacheFileName);

            // Act
            target.Remove(PackageId, PackageVersion, enableDelisting: false);

            // Assert
            Assert.Empty(target.GetAll());
        }

        [Fact]
        public void Remove_NoOpsWhenPackageDoesNotExist()
        {
            // Arrange
            var fileSystem = new Mock<IFileSystem>();
            fileSystem
                .Setup(x => x.FileExists(CacheFileName))
                .Returns(true);
            fileSystem
                .Setup(x => x.OpenFile(CacheFileName))
                .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(MinimalCacheFile)));
            var target = new ServerPackageCache(fileSystem.Object, CacheFileName);

            // Act
            target.Remove("Foo", PackageVersion, enableDelisting: false);

            // Assert
            var package = target.GetAll().FirstOrDefault();
            Assert.NotNull(package);
            Assert.Equal(PackageId, package.Id);
            Assert.Equal(PackageVersion, package.Version);
            Assert.True(package.Listed);
        }
    }
}
