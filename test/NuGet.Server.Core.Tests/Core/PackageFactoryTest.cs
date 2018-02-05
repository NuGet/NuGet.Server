// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.IO;
using NuGet.Server.Core.Tests.Infrastructure;
using Xunit;

namespace NuGet.Server.Core.Tests.Core
{
    public class PackageFactoryTest
    {
        public class Open : IDisposable
        {
            private readonly TemporaryDirectory _directory;

            public Open()
            {
                _directory = new TemporaryDirectory();
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            public void RejectsInvalidPaths(string path)
            {
                var ex = Assert.Throws<ArgumentNullException>(
                    () => PackageFactory.Open(path));
                Assert.Equal("fullPackagePath", ex.ParamName);
            }

            [Fact]
            public void InitializesPackageWithMetadata()
            {
                // Arrange
                var path = Path.Combine(_directory, "package.nupkg");
                TestData.CopyResourceToPath(TestData.PackageResource, path);

                // Act
                var package = PackageFactory.Open(path);

                // Assert
                Assert.Equal(TestData.PackageId, package.Id);
                Assert.Equal(TestData.PackageVersion, package.Version);
            }

            [Fact]
            public void InitializesPackageWithSupportedFrameworks()
            {
                // Arrange
                var path = Path.Combine(_directory, "package.nupkg");
                TestData.CopyResourceToPath(TestData.PackageResource, path);

                // Act
                var package = PackageFactory.Open(path);

                // Assert
                var frameworks = package.GetSupportedFrameworks();
                var framework = Assert.Single(frameworks);
                Assert.Equal(VersionUtility.ParseFrameworkName("net40-client"), framework);
            }

            [Fact]
            public void InitializesPackageWhichCanBeCheckedForSymbols()
            {
                // Arrange
                var path = Path.Combine(_directory, "package.nupkg");
                TestData.CopyResourceToPath(TestData.PackageResource, path);

                // Act
                var package = PackageFactory.Open(path);

                // Assert
                Assert.False(package.IsSymbolsPackage(), "The provided package is not a symbols package.");
            }

            public void Dispose()
            {
                _directory?.Dispose();
            }
        }
    }
}
