﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Linq;
using NuGet.Server.Core.DataServices;
using NuGet.Server.Core.Infrastructure;
using Xunit;

namespace NuGet.Server.Core.Tests
{
    public class PackageExtensionsTest
    {
        [Theory]
        [InlineData(true, true, false, false, 1, true, true)]
        [InlineData(false, false, true, true, 1, false, false)]
        [InlineData(true, false, false, false, 1, true, false)]
        [InlineData(false, false, true, true, 2, true, true)]
        [InlineData(true, true, false, false, 2, false, false)]
        [InlineData(false, false, true, false, 2, true, false)]
        public void AsODataPackage_PicksCorrectLatestProperties(
            bool v1AbsLatest,
            bool v1Latest,
            bool v2AbsLatest,
            bool v2Latest,
            int level,
            bool expectedAbsLatest,
            bool expectedLatest)
        {
            // Arrange
            var package = new ServerPackage
            {
                Version = new SemanticVersion("0.1.0-rc.1"),
                Authors = Enumerable.Empty<string>(),
                Owners = Enumerable.Empty<string>(),

                SemVer1IsAbsoluteLatest = v1AbsLatest,
                SemVer1IsLatest = v1Latest,
                SemVer2IsAbsoluteLatest = v2AbsLatest,
                SemVer2IsLatest = v2Latest,
            };
            var semVerLevel = new SemanticVersion(level, minor: 0, build: 0, specialVersion: null);

            // Act
            var actual = package.AsODataPackage(new ClientCompatibility(semVerLevel));

            // Assert
            Assert.Equal(expectedAbsLatest, actual.IsAbsoluteLatestVersion);
            Assert.Equal(expectedLatest, actual.IsLatestVersion);
        }
    }
}
