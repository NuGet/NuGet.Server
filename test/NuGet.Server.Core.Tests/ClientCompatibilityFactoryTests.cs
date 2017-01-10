// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using NuGet.Server.Core.Infrastructure;
using Xunit;

namespace NuGet.Server.Core.Tests
{
    public class ClientCompatibilityFactoryTests
    {
        [Theory]
        [InlineData(null, "1.0.0")]
        [InlineData("", "1.0.0")]
        [InlineData(" ", "1.0.0")]
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
    }
}
