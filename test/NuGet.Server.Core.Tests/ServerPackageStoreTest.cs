// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.Core.Tests.Infrastructure;
using Xunit;

namespace NuGet.Server.Core.Tests
{
    public class ServerPackageStoreTest
    {
        private const string PackageId = "NuGet.Versioning";
        private const string PackageVersionString = "3.5.0";
        private static readonly SemanticVersion PackageVersion = new SemanticVersion(PackageVersionString);

        [Fact]
        public void Remove_SupportsEnabledUnlisting()
        {
            // Arrange
            using (var directory = new TemporaryDirectory())
            {
                var fileSystem = new PhysicalFileSystem(directory);
                var repository = new ExpandedPackageRepository(fileSystem);
                var logger = new Infrastructure.NullLogger();

                var target = new ServerPackageStore(fileSystem, repository, logger);

                // Act
                target.Remove(PackageId, PackageVersion, enableDelisting: true);
            }
        }

        private IPackage CreatePackage(string id, SemanticVersion version)
        {
            var file = new Mock<IPackageFile>();
            file.Setup(x => x.EffectivePath).Returns("lib/net40");
            file.Setup(x => x.GetStream()).Returns(() => Stream.Null);
            file.Setup(x => x.Path).Returns($"test.dll");

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
