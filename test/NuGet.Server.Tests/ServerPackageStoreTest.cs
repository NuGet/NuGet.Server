// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.IO;
using System.Linq;
using System.Text;
using Moq;
using NuGet.Server.Infrastructure;
using Xunit;

namespace NuGet.Server.Tests
{
    public class ServerPackageStoreTest
    {
        private const string PackageId = "NuGet.Versioning";
        private const string PackageVersionString = "3.5.0";
        private const string CacheFileName = "store.json";
        private const string SemVer2VersionString = "3.5.0-rc.1+githash";
        private static readonly SemanticVersion SemVer2Version = new SemanticVersion(SemVer2VersionString);

        [Theory]
        [InlineData("[")]
        [InlineData("]")]
        [InlineData("{")]
        [InlineData("}")]
        [InlineData("[{")]
        [InlineData("[{}")]
        [InlineData("[]")]
        [InlineData("\0")]
        [InlineData("[{\"foo\": \"bar\"}]")]
        [InlineData("{\"SchemaVersion\":null,\"Packages\":[]}")]
        [InlineData("{\"SchemaVersion\":\"1.0.0\",\"Packages\":null}")]
        [InlineData("{\"SchemaVersion\":\"2.0.0\",\"Packages\":null}")]
        [InlineData("{\"SchemaVersion\":\"4.0.0\",\"Packages\":[]}")]
        [InlineData("{\"Packages\":[]}")]
        [InlineData("{\"SchemaVersion\":\"3.0.0\"}")]
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
            var actual = new ServerPackageStore(fileSystem.Object, CacheFileName);

            // Assert
            Assert.Empty(actual.GetAll());
            fileSystem.Verify(x => x.DeleteFile(CacheFileName), Times.Once);
        }

        [Theory]
        [InlineData("{\"SchemaVersion\":\"3.0.0\",\"Packages\":[]}", 0)]
        [InlineData("{\"SchemaVersion\":\"3.0.0\",\"Packages\":[{\"Id\":\"" + PackageId + "\",\"Version\":\"" + PackageVersionString + "\"}]}", 1)]
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
            var actual = new ServerPackageStore(fileSystem.Object, CacheFileName);

            // Assert
            Assert.Equal(count, actual.GetAll().Count());
            fileSystem.Verify(x => x.DeleteFile(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void Constructor_DeserializesSemVer2Version()
        {
            // Arrange
            var cacheFile = "{\"SchemaVersion\":\"3.0.0\",\"Packages\":[{\"Id\":\"" + PackageId + "\",\"Version\":\"" + SemVer2VersionString + "\"}]}";
            var fileSystem = new Mock<IFileSystem>();
            fileSystem
                .Setup(x => x.FileExists(CacheFileName))
                .Returns(true);
            fileSystem
                .Setup(x => x.OpenFile(CacheFileName))
                .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(cacheFile)));

            // Act
            var actual = new ServerPackageStore(fileSystem.Object, CacheFileName);

            // Assert
            Assert.Equal(1, actual.GetAll().Count());
            var package = actual.GetAll().First();
            Assert.Equal(SemVer2Version.ToOriginalString(), package.Version.ToOriginalString());
            Assert.Equal(SemVer2Version.ToFullString(), package.Version.ToFullString());
            Assert.Equal(SemVer2Version.ToNormalizedString(), package.Version.ToNormalizedString());
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("false", false)]
        public void Constructor_DeserializesIsSemVer2(string serialized, bool expected)
        {
            // Arrange
            var cacheFile = "{\"SchemaVersion\":\"3.0.0\",\"Packages\":[{\"Id\":\"" + PackageId + "\",\"Version\":\"" + SemVer2VersionString + "\",\"IsSemVer2\":" + serialized + "}]}";
            var fileSystem = new Mock<IFileSystem>();
            fileSystem
                .Setup(x => x.FileExists(CacheFileName))
                .Returns(true);
            fileSystem
                .Setup(x => x.OpenFile(CacheFileName))
                .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(cacheFile)));

            // Act
            var actual = new ServerPackageStore(fileSystem.Object, CacheFileName);

            // Assert
            Assert.Equal(1, actual.GetAll().Count());
            var package = actual.GetAll().First();
            Assert.Equal(expected, package.IsSemVer2);
        }

        [Fact]
        public void Persist_RetainsSemVer2Version()
        {
            // Arrange
            var fileSystem = new Mock<IFileSystem>();
            fileSystem
                .Setup(x => x.FileExists(CacheFileName))
                .Returns(false);

            var memoryStream = new MemoryStream();
            fileSystem
                .Setup(x => x.CreateFile(CacheFileName))
                .Returns(memoryStream);

            var actual = new ServerPackageStore(fileSystem.Object, CacheFileName);
            actual.Store(new ServerPackage
            {
                Id = PackageId,
                Version = SemVer2Version
            });

            // Act
            actual.Persist();

            // Assert
            var content = Encoding.UTF8.GetString(memoryStream.ToArray());
            Assert.Contains(SemVer2VersionString, content);
        }
    }
}
