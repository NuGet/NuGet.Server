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
    public class ServerPackageStoreTest
    {
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
            var fileName = "store.json";
            var fileSystem = new Mock<IFileSystem>();
            fileSystem
                .Setup(x => x.FileExists(fileName))
                .Returns(true);
            fileSystem
                .Setup(x => x.OpenFile(fileName))
                .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(content)));

            // Act
            var actual = new ServerPackageStore(fileSystem.Object, fileName);

            // Assert
            Assert.Empty(actual.GetAll());
            fileSystem.Verify(x => x.DeleteFile(fileName), Times.Once);
        }

        [Theory]
        [InlineData("[]", 0)]
        [InlineData("[{\"Id\":\"NuGet.Versioning\",\"Version\":\"3.5.0\"}]", 1)]
        public void Constructor_LeavesValidCacheFile(string content, int count)
        {
            // Arrange
            var fileName = "store.json";
            var fileSystem = new Mock<IFileSystem>();
            fileSystem
                .Setup(x => x.FileExists(fileName))
                .Returns(true);
            fileSystem
                .Setup(x => x.OpenFile(fileName))
                .Returns(() => new MemoryStream(Encoding.UTF8.GetBytes(content)));

            // Act
            var actual = new ServerPackageStore(fileSystem.Object, fileName);

            // Assert
            Assert.Equal(count, actual.GetAll().Count());
            fileSystem.Verify(x => x.DeleteFile(It.IsAny<string>()), Times.Never);
        }
    }
}
