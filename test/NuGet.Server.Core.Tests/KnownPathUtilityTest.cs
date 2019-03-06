// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using NuGet.Server.Core.Infrastructure;
using Xunit;

namespace NuGet.Server.Core.Tests
{
    public class KnownPathUtilityTest
    {
        public class TryParseFileName
        {
            [Theory]
            [InlineData(@"NuGet.Core\2.12.0\NuGet.Core.2.12.0.nupkg", true, "NuGet.Core", "2.12.0")]
            [InlineData(@"NuGet.Core\2.12.0-BETA\NuGet.Core.2.12.0-BETA.nupkg", true, "NuGet.Core", "2.12.0-BETA")]
            [InlineData(@"NuGet.Core\2.12.0\NuGet.Core.2.12.0.nupkg.sha512", true, "NuGet.Core", "2.12.0")]
            [InlineData(@"NuGet.Core\2.12.0\NuGet.Core.nuspec", true, "NuGet.Core", "2.12.0")]
            [InlineData(@"NuGet.Core\2.12.0-BETA\NuGet.Core.nuspec", true, "NuGet.Core", "2.12.0-BETA")]
            [InlineData(@"C:\my\packages\NuGet.Core\2.12.0\NuGet.Core.2.12.0.nupkg", false, null, null)]
            [InlineData(@"packages\NuGet.Core\2.12.0\NuGet.Core.2.12.0.nupkg", false, null, null)]
            [InlineData(@"C:\packages\NuGet.Core\2.12.0\NuGet.Core.2.12.0.nupkg", false, null, null)]
            [InlineData(@"C:\NuGet.Core\2.12.0\NuGet.Core.2.12.0.nupkg", false, null, null)]
            [InlineData(@"C:\2.12.0\NuGet.Core.2.12.0.nupkg", false, null, null)]
            [InlineData(@"NuGet.Core\2.12.0\NuGet.Core.2.12.0.sha512", false, "NuGet.Core", "2.12.0")]
            [InlineData(@"NuGet.Core\2.12.0-beta\NuGet.Core.2.12.0-BETA.nupkg", false, "NuGet.Core", "2.12.0-beta")]
            [InlineData(@"NuGet.Core\2.12.0\nuget.core.nuspec", false, "NuGet.Core", "2.12.0")]
            [InlineData(@"NuGet.Core\2.12.0\NuGet.Core.nupkg", false, "NuGet.Core", "2.12.0")]
            [InlineData(@"NuGet.Core\2.12.0\NuGet.Core.nupkg.sha512", false, "NuGet.Core", "2.12.0")]
            [InlineData(@"NuGet.Core\2.12.0.0\NuGet.Core.2.12.0.0.nupkg", false, "NuGet.Core", "2.12.0.0")]
            [InlineData(@"NuGet.Core\2.12.0\NuGet.Core.2.12.0.png", false, "NuGet.Core", "2.12.0")]
            [InlineData(@"root\NuGet.Core\NuGet.Core.2.12.0.nupkg", false, "root", null)]
            [InlineData(@"NuGet.Core\NuGet.Core.2.12.0.nupkg", false, null, null)]
            public void WorksAsExpected(string path, bool expected, string id, string unparsedVersion)
            {
                // Arrange
                var version = unparsedVersion == null ? null : SemanticVersion.Parse(unparsedVersion);

                // Act
                var actual = KnownPathUtility.TryParseFileName(path, out var actualId, out var actualVersion);

                // Assert
                Assert.Equal(expected, actual);
                Assert.Equal(id, actualId);
                Assert.Equal(version, actualVersion);
                Assert.Equal(version?.ToFullString(), actualVersion?.ToFullString());
            }
        }
    }
}
