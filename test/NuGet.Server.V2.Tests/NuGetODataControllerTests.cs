// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Http.OData;
using System.Web.Http.OData.Query;
using System.Web.Http.Results;
using Moq;
using NuGet.Server.Core.DataServices;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.Model;
using NuGet.Server.V2.Tests.Infrastructure;
using NuGet.Server.V2.Tests.TestUtils;
using Xunit;

namespace NuGet.Server.V2.Tests
{
    public class NuGetODataControllerTests
    {
        internal static ServerPackage CreatePackageWithDefaults(string id, string version, bool listed=true, string supportedFrameWorks=null, IEnumerable<string> authors=null, IEnumerable<string> owners=null)
        {
            var serverPackage = new ServerPackage()
            {
                Id=id,
                Version = SemanticVersion.Parse(version),
                Listed = listed,
                SupportedFrameworks=supportedFrameWorks,
                Authors= authors ?? Enumerable.Empty<string>(),
                Owners = owners ?? Enumerable.Empty<string>(),
            };
            return serverPackage;
        }

        internal static void AssertPackage(dynamic expected, ODataPackage package)
        {
            Assert.Equal(expected.Id, package.Id);
            Assert.Equal(expected.Version, package.Version);
        }

        public class PackagesCollection
        {
            [Theory]
            [InlineData("Id eq 'Foo'", 100, 2, new[] { "Foo", "Foo" }, new[] { "1.0.0", "1.0.1-a" })]
            [InlineData("Id eq 'Bar'", 1, 1, new[] { "Bar" }, new[] { "1.0.0" })]
            [InlineData("Id eq 'Bar' and IsPrerelease eq true", 100, 2, new[] { "Bar", "Bar" }, new[] { "2.0.1-a", "2.0.1-b" })]
            [InlineData("Id eq 'Bar' or Id eq 'Foo'", 100, 6, new[] { "Foo", "Foo", "Bar", "Bar", "Bar", "Bar" }, new[] { "1.0.0", "1.0.1-a", "1.0.0", "2.0.0", "2.0.1-a", "2.0.1-b" })]
            public void PackagesReturnsCollection(string filter, int top, int expectedNumberOfPackages, string[] expectedIds, string[] expectedVersions)
            {
                // Arrange
                var repo = ControllerTestHelpers.SetupTestPackageRepository();
                var v2Service = new TestableNuGetODataController(repo.Object);

                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Packages?$filter=" + filter + "&$top=" + top);

                // Act
                var result = (v2Service.Get(new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request)))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(expectedNumberOfPackages, result.Length);
                for (var i = 0; i < expectedIds.Length; i++)
                {
                    var expectedId = expectedIds[i];
                    var expectedVersion = expectedVersions[i];

                    Assert.True(result.Any(p => p.Id == expectedId && p.Version == expectedVersion), string.Format("Search results did not contain {0} {1}", expectedId, expectedVersion));
                }
            }

            [Theory]
            [InlineData("Id eq 'Foo'", 100, 2)]
            [InlineData("Id eq 'Bar'", 1, 1)]
            [InlineData("Id eq 'Bar' and IsPrerelease eq true", 100, 2)]
            [InlineData("Id eq 'Bar' or Id eq 'Foo'", 100, 6)]
            [InlineData("Id eq 'NotBar'", 100, 0)]
            public void PackagesCountReturnsCorrectCount(string filter, int top, int expectedNumberOfPackages)
            {
                // Arrange
                var repo = ControllerTestHelpers.SetupTestPackageRepository();
                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Packages/$count?$filter=" + filter + "&$top=" + top);

                // Act
                var result = (v2Service.GetCount(new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request)))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectResult<PlainTextResult>();

                // Assert
                Assert.Equal(expectedNumberOfPackages.ToString(), result.Content);
            }

            [Fact]
            public void PackagesCountReturnsCorrectCountForDeletedPackages()
            {
                PackagesCountReturnsCorrectCount("Id eq 'Baz'", 100, 0);
            }

            [Theory]
            [InlineData("Foo", "1.0.0")]
            [InlineData("Foo", "1.0.1-a")]
            [InlineData("Bar", "1.0.0")]
            [InlineData("Bar", "2.0.0")]
            [InlineData("Bar", "2.0.1-b")]
            public void PackagesByIdAndVersionReturnsPackage(string expectedId, string expectedVersion)
            {
                // Arrange
                var repo = ControllerTestHelpers.SetupTestPackageRepository();
                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Packages(Id='" + expectedId + "', Version='" + expectedVersion + "')");

                // Act
                var result = (v2Service.Get(new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request), expectedId, expectedVersion))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<ODataPackage>();

                // Assert
                Assert.Equal(expectedId, result.Id);
                Assert.Equal(expectedVersion, result.Version);
            }

            [Theory]
            [InlineData("NoFoo", "1.0.0")]
            [InlineData("NoBar", "1.0.0-a")]
            [InlineData("Bar", "9.9.9")]
            public void PackagesByIdAndVersionReturnsNotFoundWhenPackageNotFound(string expectedId, string expectedVersion)
            {
                // Arrange
                var repo = ControllerTestHelpers.SetupTestPackageRepository();

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Packages(Id='" + expectedId + "', Version='" + expectedVersion + "')");

                //// Act
                v2Service.Get(new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request), expectedId, expectedVersion)
                    .ExpectResult<NotFoundResult>();
            }

            [Fact]
            public void PackagesByIdAndVersionReturnsNotFoundWhenPackageIsDeleted()
            {
                PackagesByIdAndVersionReturnsNotFoundWhenPackageNotFound("Baz", "1.0.0");
            }

            [Theory]
            [InlineData("Id eq 'Baz'")]
            public void PackagesCollectionDoesNotContainDeletedPackages(string filter)
            {
                // Arrange
                var repo = ControllerTestHelpers.SetupTestPackageRepository();
                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Packages?$filter=" + filter);

                // Act
                var result = (v2Service.Get(new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request)))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.False(result.Any(p => p.Id == "Baz"));
            }

        }

        public class FindPackagesByIdMethod
        {
            [Fact]
            public void FindPackagesByIdReturnsUnlistedAndPrereleasePackages()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        new ServerPackage
                            {
                                Id = "Foo",
                                Version = SemanticVersion.Parse("1.0.0"),
                                //IsPrerelease = false,
                                Listed = false,
                                Authors = new [] { string.Empty },
                                Owners = new [] { string.Empty },
                                Description = string.Empty,
                                Summary = string.Empty,
                                Tags = string.Empty
                            },
                        new ServerPackage
                            {
                                Id = "Foo",
                                Version = SemanticVersion.Parse("1.0.1-a"),
                                //IsPrerelease = true,
                                Listed = true,
                                Authors = new [] { string.Empty },
                                Owners = new [] { string.Empty },
                                Description = string.Empty,
                                Summary = string.Empty,
                                Tags = string.Empty
                            },
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (v2Service.FindPackagesById(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo"))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(2, result.Count());
                Assert.Equal("Foo", result.First().Id);
                Assert.Equal("1.0.0", result.First().Version);

                Assert.Equal("Foo", result.Last().Id);
                Assert.Equal("1.0.1-a", result.Last().Version);
            }

            [Fact]
            public void FindPackagesByIdReturnsEmptyCollectionWhenNoPackages()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(() => Enumerable.Empty<ServerPackage>().AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (v2Service.FindPackagesById(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo"))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(0, result.Count());
            }

            [Fact]
            public void FindPackagesByIdDoesNotHitBackendWhenIdIsEmpty()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Loose);

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (v2Service.FindPackagesById(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    ""))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                repo.Verify(r => r.GetPackages(), Times.Never);
                Assert.Equal(0, result.Count());
            }

            [Fact]
            public void FindPackagesByIdDoesNotReturnDeletedPackages()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        new ServerPackage
                            {
                                Id = "Foo",
                                Version = SemanticVersion.Parse("1.0.0"),
                                Authors= Enumerable.Empty<string>(),
                                Owners= Enumerable.Empty<string>(),
                                Listed = false,
                            },
                        new ServerPackage
                            {
                                Id = "Foo",
                                Version = SemanticVersion.Parse("1.0.1"),
                                Authors= Enumerable.Empty<string>(),
                                Owners= Enumerable.Empty<string>(),
                                //IsPrerelease = false,
                                Listed = true,
                                //Deleted = true
                            },
                    }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (v2Service.FindPackagesById(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo"))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(0, result.Count());
            }
        }

        public class SearchMethod
        {
            [Theory]
            [InlineData("Foo", false, 1, new[] { "Foo" }, new[] { "1.0.0" })]
            [InlineData("Bar", false, 2, new[] { "Bar", "Bar" }, new[] { "1.0.0", "2.0.0" })]
            [InlineData("", false, 3, new[] { "Foo", "Bar", "Bar" }, new[] { "1.0.0", "1.0.0", "2.0.0" })]
            [InlineData("CommonTag", false, 3, new[] { "Foo", "Bar", "Bar" }, new[] { "1.0.0", "1.0.0", "2.0.0" })]
            [InlineData("", true, 5, new[] { "Foo", "Foo", "Bar", "Bar", "Bar" }, new[] { "1.0.0", "1.0.1-a", "1.0.0", "2.0.0", "2.0.1-a" })]
            public void SearchFiltersPackagesBySearchTermAndPrereleaseFlag(string searchTerm, bool includePrerelease, int expectedNumberOfPackages, string[] expectedIds, string[] expectedVersions)
            {
                // Arrange
                var repo = ControllerTestHelpers.SetupTestPackageRepository();
                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Search()?searchTerm='" + searchTerm + "'&targetFramework=''&includePrerelease=false");

                // Act
                var result = (v2Service.Search(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    searchTerm: searchTerm,
                    targetFramework: null,
                    includePrerelease: includePrerelease))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(expectedNumberOfPackages, result.Length);
                for (var i = 0; i < expectedIds.Length; i++)
                {
                    var expectedId = expectedIds[i];
                    var expectedVersion = expectedVersions[i];

                    Assert.True(result.Any(p => p.Id == expectedId && p.Version == expectedVersion), string.Format("Search results did not contain {0} {1}", expectedId, expectedVersion));
                }
            }

            [Theory]
            [InlineData("Foo", false, 1)]
            [InlineData("Bar", false, 2)]
            [InlineData("", false, 3)]
            [InlineData("CommonTag", false, 3)]
            [InlineData("", true, 5)]
            public void SearchCountFiltersPackagesBySearchTermAndPrereleaseFlag(string searchTerm, bool includePrerelease, int expectedNumberOfPackages)
            {
                // Arrange
                var repo = ControllerTestHelpers.SetupTestPackageRepository();
                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Search()?searchTerm='" + searchTerm + "'&targetFramework=''&includePrerelease=false");

                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Search()/$count?searchTerm='" + searchTerm + "'&targetFramework=''&includePrerelease=false");

                // Act
                var result = (v2Service.SearchCount(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    searchTerm: searchTerm,
                    targetFramework: null,
                    includePrerelease: includePrerelease))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectResult<PlainTextResult>();

                // Assert
                Assert.Equal(expectedNumberOfPackages.ToString(), result.Content);
            }

            [Fact]
            public void SearchCountDoesNotCountDeletedPackages()
            {
                SearchCountFiltersPackagesBySearchTermAndPrereleaseFlag("Baz", true, 0);
            }
        }

        public class GetUpdatesMethod
        {
            [Theory]
            [InlineData(null, "1.0.0|0.9")]
            [InlineData("", "1.0.0|0.9")]
            [InlineData("   ", "1.0.0|0.9")]
            [InlineData("|   ", "1.0.0|0.9")]
            [InlineData("A", null)]
            [InlineData("A", "")]
            [InlineData("A", "   |")]
            [InlineData("A", "|  ")]
            [InlineData("A|B", "1.0|")]
            public void GetUpdatesReturnsEmptyResultsIfInputIsMalformed(string id, string version)
            {
                // Arrange
                var repo = Mock.Of<IServerPackageRepository>();
                var v2Service = new TestableNuGetODataController(repo);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    id,
                    version,
                    includePrerelease: false,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: null)
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Empty(result);
            }

            [Fact]
            public void GetUpdatesIgnoresItemsWithMalformedVersions()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0-alpha", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true) ,
                }.AsQueryable());
                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|abcd",
                    includePrerelease: false,
                    includeAllVersions: false,
                    targetFrameworks: null,
                    versionConstraints: null)
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(1, result.Count());
                AssertPackage(new { Id = "Foo", Version = "1.2.0" }, result.First());
            }

            [Fact]
            public void GetUpdatesReturnsVersionsNewerThanListedVersion()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0-alpha", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true),
                        CreatePackageWithDefaults("Qux", "1.1.0", listed: true) ,
                }.AsQueryable());
                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: null)
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(3, result.Length);
                AssertPackage(new { Id = "Foo", Version = "1.1.0" }, result[0]);
                AssertPackage(new { Id = "Foo", Version = "1.2.0" }, result[1]);
                AssertPackage(new { Id = "Qux", Version = "2.0" }, result[2]);
            }

            [Theory]
            [InlineData("2.3|3.5|(1.0,2.3)")]
            [InlineData("2.3")]
            [InlineData("1.0||2.0")]
            [InlineData("||")]
            [InlineData("|1.0|")]
            public void GetUpdatesReturnsEmptyIfVersionConstraintsContainWrongNumberOfElements(string constraintString)
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0-alpha", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true) ,
                        CreatePackageWithDefaults("Qux", "3.0", listed: true) ,
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: constraintString)
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(0, result.Count());
            }

            [Fact]
            public void GetUpdatesReturnsVersionsConformingToConstraints()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0-alpha", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true) ,
                        CreatePackageWithDefaults("Qux", "3.0", listed: true) ,
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: "(,1.2.0)|[2.0,2.3]")
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(2, result.Length);
                AssertPackage(new { Id = "Foo", Version = "1.1.0" }, result[0]);
                AssertPackage(new { Id = "Qux", Version = "2.0" }, result[1]);
            }

            [Fact]
            public void GetUpdatesIgnoreInvalidVersionConstraints()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0-alpha", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true) ,
                        CreatePackageWithDefaults("Qux", "3.0", listed: true) ,
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: "(,1.2.0)|abdfsdf")
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(3, result.Length);
                AssertPackage(new { Id = "Foo", Version = "1.1.0" }, result[0]);
                AssertPackage(new { Id = "Qux", Version = "2.0" }, result[1]);
                AssertPackage(new { Id = "Qux", Version = "3.0" }, result[2]);
            }

            [Fact]
            public void GetUpdatesReturnsVersionsConformingToConstraintsWithMissingConstraintsForSomePackges()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0-alpha", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true) ,
                        CreatePackageWithDefaults("Qux", "3.0", listed: true) ,
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: false,
                    targetFrameworks: null,
                    versionConstraints: "|(1.2,2.8)")
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(2, result.Length);
                AssertPackage(new { Id = "Foo", Version = "1.2.0" }, result[0]);
                AssertPackage(new { Id = "Qux", Version = "2.0" }, result[1]);
            }

            [Fact]
            public void GetUpdatesReturnsEmptyPackagesIfNoPackageSatisfiesConstraints()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0-alpha", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true) ,
                        CreatePackageWithDefaults("Qux", "3.0", listed: true) ,
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: false,
                    targetFrameworks: null,
                    versionConstraints: "3.4|4.0")
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(0, result.Count());
            }

            [Fact]
            public void GetUpdatesReturnsCaseInsensitiveMatches()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0-alpha", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true),
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "foo",
                    "1.0.0",
                    includePrerelease: false,
                    includeAllVersions: false,
                    targetFrameworks: null,
                    versionConstraints: null)
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(1, result.Length);
                Assert.Equal("Foo", result[0].Id);
                Assert.Equal("1.2.0", result[0].Version);
            }

            [Fact]
            public void GetUpdatesReturnsUpdateIfAnyOfTheProvidedVersionsIsOlder()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0-alpha", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true) ,
                        CreatePackageWithDefaults("Qux", "3.0", listed: true) ,
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Foo|Qux",
                    "1.0.0|1.2.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: false,
                    targetFrameworks: null,
                    versionConstraints: null)
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(2, result.Length);
                AssertPackage(new { Id = "Foo", Version = "1.2.0" }, result[0]);
                AssertPackage(new { Id = "Qux", Version = "3.0" }, result[1]);
            }

            [Fact]
            public void GetUpdatesReturnsPrereleasePackages()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0-alpha", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true) ,
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.1.0|0.9",
                    includePrerelease: true,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: null)
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(3, result.Length);
                AssertPackage(new { Id = "Foo", Version = "1.2.0" }, result[0]);
                AssertPackage(new { Id = "Foo", Version = "1.2.0-alpha" }, result[1]);
                AssertPackage(new { Id = "Qux", Version = "2.0" }, result[2]);
            }

            [Fact]
            public void GetUpdatesDoesNotReturnDeletedPackages()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true),
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo",
                    "1.0.0",
                    includePrerelease: true,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: null)
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(0, result.Length);
            }

            [Fact]
            public void GetUpdatesReturnsResultsIfDuplicatesInPackageList()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0-alpha", listed: true),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true) ,
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);

                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result =
                    v2Service.GetUpdates(
                        new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                        "Foo|Qux|Foo",
                        "0.9|1.5|1.1.2",
                        includePrerelease: false,
                        includeAllVersions: true,
                        targetFrameworks: null,
                        versionConstraints: null)
                        .ExpectQueryResult<ODataPackage>()
                        .GetInnerResult()
                        .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                        .ToArray();

                // Assert
                Assert.Equal(4, result.Length);
                AssertPackage(new { Id = "Foo", Version = "1.0.0" }, result[0]);
                AssertPackage(new { Id = "Foo", Version = "1.1.0" }, result[1]);
                AssertPackage(new { Id = "Foo", Version = "1.2.0" }, result[2]);
                AssertPackage(new { Id = "Qux", Version = "2.0" }, result[3]);
            }

            [Fact]
            public void GetUpdatesFiltersByTargetFramework()
            {
                // Arrange
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true, supportedFrameWorks: "SL5,Net40-Full"),
                        CreatePackageWithDefaults("Foo", "1.3.0-alpha", listed: true, supportedFrameWorks: "SL5,Net40-Full"),
                        CreatePackageWithDefaults("Foo", "2.0.0", listed: true, supportedFrameWorks: "SL5,WinRT"),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true),
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object); v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0|1.5",
                    includePrerelease: false,
                    includeAllVersions: true,
                    targetFrameworks: "net40",
                    versionConstraints: null)
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(2, result.Length);
                AssertPackage(new { Id = "Foo", Version = "1.1.0" }, result[0]);
                AssertPackage(new { Id = "Qux", Version = "2.0" }, result[1]);
            }

            [Fact]
            public void GetUpdatesFiltersIncludesHighestPrereleasePackage()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackages()).Returns(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true, supportedFrameWorks: "SL5,Net40-Full"),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true, supportedFrameWorks: "SL5,Net40-Full"),
                        CreatePackageWithDefaults("Foo", "1.3.0-alpha", listed: true, supportedFrameWorks: "SL5,Net40-Full"),
                        CreatePackageWithDefaults("Foo", "2.0.0", listed: true, supportedFrameWorks: "SL5,WinRT"),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true),
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0|1.5",
                    includePrerelease: true,
                    includeAllVersions: false,
                    targetFrameworks: "net40",
                    versionConstraints: null)
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(2, result.Length);
                AssertPackage(new { Id = "Foo", Version = "1.3.0-alpha" }, result[0]);
                AssertPackage(new { Id = "Qux", Version = "2.0" }, result[1]);
            }
        }
    }



}
