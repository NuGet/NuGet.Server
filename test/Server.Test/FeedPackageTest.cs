using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Versioning;
using NuGet;
using NuGet.Server.Infrastructure;
using Xunit;

namespace Server.Test
{
    public class FeedPackageTest
    {
        [Fact]
        public void FeedPackageHasSameMembersAsDataServicePackage()
        {
            // Arrange
            // This is not pretty but it's the most effective way.
            var excludedProperties = new[] { "Owners", "ReportAbuseUrl", "GalleryDetailsUrl", "DownloadUrl", "Rating", "RatingsCount", "Language", 
                                             "AssemblyReferences", "FrameworkAssemblies", "DependencySets", "PackageAssemblyReferences", "LicenseNames",
                                             "LicenseNameCollection", "LicenseReportUrl"
            };
            var feedPackageProperties = new HashSet<string>(typeof(NuGet.Server.DataServices.Package).GetProperties().Select(p => p.Name), StringComparer.Ordinal);
            var dataServiceProperties = typeof(DataServicePackage).GetProperties()
                                                                  .Select(p => p.Name)
                                                                  .ToList();

            // Assert
            // Assert.Equal(feedPackageProperties.Count, dataServiceProperties.Count);
            foreach (var property in dataServiceProperties)
            {
                if (excludedProperties.Contains(property))
                {
                    continue;
                }
                Assert.True(feedPackageProperties.Contains(property), String.Format(CultureInfo.InvariantCulture,
                    "Property {0} could not be found in NuGet.Server package.", property));
            }
        }

        [Fact]
        public void FeedPackageSerializeDependenciesWithTargetFrameworkCorrectly()
        {
            // Arrange
            var corePackage = NuGet.Test.PackageUtility.CreatePackageWithDependencySets(
                "A", 
                "1.0",
                dependencySets: new PackageDependencySet[] {
                    new PackageDependencySet(new FrameworkName(".NETFramework, Version=2.0"),
                                             new [] { new PackageDependency("B") }),

                    new PackageDependencySet(new FrameworkName(".NETFramework, Version=3.0"),
                                             new [] { new PackageDependency("B"), 
                                                      new PackageDependency("C", VersionUtility.ParseVersionSpec("2.0")) }),

                    new PackageDependencySet((FrameworkName)null,
                                             new [] { new PackageDependency("D", VersionUtility.ParseVersionSpec("(1.0,3.0-alpha]")) }),

                    new PackageDependencySet(new FrameworkName(".NETCore, Version=4.5"),
                                             new PackageDependency[0]),

                    new PackageDependencySet((FrameworkName)null,
                                             new [] { new PackageDependency("X") })
                });

            // Act
            var package = new NuGet.Server.DataServices.Package(corePackage, new DerivedPackageData());

            // Assert
            Assert.Equal(@"B::net20|B::net30|C:2.0:net30|D:(1.0, 3.0-alpha]|::win|X", package.Dependencies);
        }
    }
}
