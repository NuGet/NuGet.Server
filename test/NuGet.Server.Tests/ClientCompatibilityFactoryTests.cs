// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using NuGet.Server.DataServices;
using Xunit;

namespace NuGet.Server.Tests
{
    public class ClientCompatibilityFactoryTests
    {
        [Theory]
        [InlineData("/", "1.0.0")]
        [InlineData("/Packages", "1.0.0")]
        [InlineData("/Packages?semVerLevel=2", "1.0.0")]
        [InlineData("/Packages?semVerLevel=3", "1.0.0")]
        [InlineData("/Packages?semVerLevel=100000000000000", "1.0.0")]
        [InlineData("/Packages()", "1.0.0")]
        [InlineData("/Packages()?semVerLevel=0.0.0", "0.0.0")]
        [InlineData("/Packages()?semVerLevel=1.0.0", "1.0.0")]
        [InlineData("/Packages()?semVerLevel=2.0.0", "2.0.0")]
        [InlineData("/Packages()?semVerLevel=2.0.0-rc.1", "2.0.0-rc.1")]
        [InlineData("/Packages()?semVerLevel=3.0.0", "3.0.0")]
        [InlineData("/Packages()?SEMVERLEVEL=2.0.0", "2.0.0")]
        [InlineData("/Packages()?semverlevel=2.0.0", "2.0.0")]
        [InlineData("/Packages()?semVerLevel", "1.0.0")]
        [InlineData("/Packages()?semVerLevel=", "1.0.0")]
        [InlineData("/Packages()?semVerLevel=a", "1.0.0")]
        [InlineData("/FindPackagesById()?semVerLevel=1.0.0", "1.0.0")]
        [InlineData("/FindPackagesById()?semVerLevel=2.0.0", "2.0.0")]
        [InlineData("/Packages(Id='Newtonsoft.Json',Version='9.0.1')?semVerLevel=0.0.0", "2.0.0")]
        [InlineData("/Packages(Id='Newtonsoft.Json',Version='9.0.1')?semVerLevel=1.0.0", "2.0.0")]
        [InlineData("/Packages(Id='Newtonsoft.Json',Version='9.0.1')?semVerLevel=2.0.0", "2.0.0")]
        [InlineData("/Packages(Id='Newtonsoft.Json',Version='9.0.1')?semVerLevel=3.0.0", "2.0.0")]
        [InlineData("/Packages(Id='A',Version='1.0.0')?semVerLevel=", "2.0.0")]
        [InlineData("/Packages( Id = 'A' , Version = '1.0.0' )", "2.0.0")]
        public void FromUri_DetectsSemVerLevel(string relative, string unparsedExpected)
        {
            // Arrange
            var uri = new Uri(new Uri("http://localhost:8080/nuget/"), relative);
            var expected = new SemanticVersion(unparsedExpected);

            // Act
            var actual = ClientCompatibilityFactory.FromUri(uri);

            // Assert
            Assert.Equal(expected, actual.SemVerLevel);
        }

        [Theory]
        [InlineData(null, "1.0.0")]
        [InlineData("0", "1.0.0")]
        [InlineData("a", "1.0.0")]
        [InlineData("0.0.0", "0.0.0")]
        [InlineData("1.0.0", "1.0.0")]
        [InlineData("2.0.0", "2.0.0")]
        [InlineData("2.0.0-rc.1", "2.0.0-rc.1")]
        [InlineData("3.0.0", "3.0.0")]
        public void FromProperties_SetsSemVerLevel(string semVerLevel, string unparsedExpected)
        {
            // Arrange
            var expected = new SemanticVersion(unparsedExpected);

            // Arrange & Act
            var actual = ClientCompatibilityFactory.FromProperties(semVerLevel);

            // Assert
            Assert.Equal(expected, actual.SemVerLevel);
        }

        [Fact]
        public void FromUri_AllowsNullUri()
        {
            // Arrange & Act
            var actual = ClientCompatibilityFactory.FromUri(uri: null);

            // Assert
            Assert.Equal(new SemanticVersion("1.0.0"), actual.SemVerLevel);
        }

        [Fact]
        public void FromProperties_AllowsNullSemVerLevel()
        {
            // Arrange & Act
            var actual = ClientCompatibilityFactory.FromProperties(unparsedSemVerLevel: null);

            // Assert
            Assert.Equal(new SemanticVersion("1.0.0"), actual.SemVerLevel);
        }
    }
}
