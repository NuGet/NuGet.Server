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
                var package = new ServerPackage
                {
                    Id = "Package" + i,
                    Version = new SemanticVersion(1, 0, i, 0),
                    Title = "Title" + i,
                    Authors = new[] { "Author" + i },
                    Owners = new[] { "Owner" + i },
                    IconUrl = new Uri("urn:icon"),
                    LicenseUrl = new Uri("urn:license"),
                    ProjectUrl = new Uri("urn:project"),
                    RequireLicenseAcceptance = true,
                    DevelopmentDependency = true,
                    Description = "Description" + i,
                    Summary = "Summary" + i,
                    ReleaseNotes = "ReleaseNotes" + i,
                    Language = "Language" + i,
                    Tags = "Tags" + i,
                    Copyright = "Copyright" + i,
                    MinClientVersion = null,
                    ReportAbuseUrl = new Uri("urn:abuse"),
                    DownloadCount = 0,
                    IsAbsoluteLatestVersion = true,
                    IsLatestVersion = true,
                    Listed = true,
                    Dependencies = string.Empty,
                    SupportedFrameworks = string.Empty,
                    PackageSize = 1234,
                    PackageHash = "Hash" + i,
                    PackageHashAlgorithm = "HashAlgorithm" + i,
                    LastUpdated = DateTimeOffset.UtcNow,
                    Created = DateTimeOffset.UtcNow,
                    FullPath = "FullPath" + i
                };

                // Preload collections
                package.DependencySets.Any();
                package.SupportedFrameworks.Any();

                originalPackages.Add(package);
            }

            return originalPackages;
        }
    }
}