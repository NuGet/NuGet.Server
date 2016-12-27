// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NuGet.Server.Core.Infrastructure;
using Xunit;

namespace NuGet.Server.Core.Tests
{
    public class JsonNetPackagesSerializerTests
    {
        [Fact]
        public void TestSerializationRoundTrip()
        {
            // Arrange
            var originalPackages = GenerateServerPackages(100);
            var serializer = new JsonNetPackagesSerializer();

            // Act
            var deserializedPackages = new List<ServerPackage>();
            using (var memoryStream = new MemoryStream())
            {
                serializer.Serialize(originalPackages, memoryStream);

                memoryStream.Position = 0;

                deserializedPackages.AddRange(serializer.Deserialize(memoryStream));
            }

            // Assert
            Assert.Equal(originalPackages.Count, deserializedPackages.Count);
            for (var i = 0; i < originalPackages.Count; i++)
            {
                Assert.True(PublicPropertiesEqual(originalPackages[i], deserializedPackages[i], "DependencySets", "FrameworkAssemblies", "PackageAssemblyReferences", "AssemblyReferences"));
            }
        }

        private static bool PublicPropertiesEqual<T>(T a, T b, params string[] ignoreProperties) where T : class
        {
            if (a != null && b != null)
            {
                var type = typeof(T);

                foreach (var pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!ignoreProperties.Contains(pi.Name))
                    {
                        var selfValue = type.GetProperty(pi.Name).GetValue(a, null);
                        var toValue = type.GetProperty(pi.Name).GetValue(b, null);

                        if (selfValue != toValue && (selfValue == null || !selfValue.Equals(toValue)) && !(selfValue is IEnumerable))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            return a == b;
        }

        private static List<ServerPackage> GenerateServerPackages(int count)
        {
            var originalPackages = new List<ServerPackage>();

            for (var i = 0; i < count; i++)
            {
                var package = new ServerPackage(
                    id: "Package" + i,
                    version: new SemanticVersion(1, 0, i, 0),
                    title: "Title" + i,
                    authors: new [] { "Author" + i },
                    owners: new [] { "Owner" + i },
                    iconUrl: new Uri("urn:icon"),
                    licenseUrl: new Uri("urn:license"),
                    projectUrl: new Uri("urn:project"),
                    requireLicenseAcceptance: true,
                    developmentDependency: true,
                    description: "Description" + i,
                    summary: "Summary" + i,
                    releaseNotes: "ReleaseNotes" + i,
                    language: "Language" + i,
                    tags: "Tags" + i,
                    copyright: "Copyright" + i,
                    minClientVersion: null,
                    reportAbuseUrl: new Uri("urn:abuse"),
                    downloadCount: 0,
                    isAbsoluteLatestVersion: true,
                    isLatestVersion: true,
                    listed: true,
                    published: null,
                    dependencies: string.Empty,
                    supportedFrameworks: string.Empty,
                    packageSize: 1234,
                    packageHash: "Hash" + i,
                    packageHashAlgorithm: "HashAlgorithm" + i,
                    lastUpdated: DateTimeOffset.UtcNow,
                    created: DateTimeOffset.UtcNow,
                    fullPath: "FullPath" + i
                    );

                originalPackages.Add(package);
            }

            return originalPackages;
        }
    }
}