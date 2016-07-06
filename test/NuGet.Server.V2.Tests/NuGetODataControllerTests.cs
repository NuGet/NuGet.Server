using Moq;
using NuGet.Server.Core.DataServices;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Results;
using System.Web.Http.OData;
using System.Web.Http.OData.Query;
using Xunit;
using NuGet.Server.V2.Tests.TestUtils;
using NuGet.Server.V2.Model;

namespace NuGet.Server.V2.Tests
{
    public class NuGetODataControllerTests
    {
        public class ThePackagesCollection
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
                for (int i = 0; i < expectedIds.Length; i++)
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

        public class TheFindPackagesByIdMethod
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

    }



}
