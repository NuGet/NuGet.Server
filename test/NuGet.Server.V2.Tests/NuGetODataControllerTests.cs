// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.OData;
using System.Web.Http.OData.Query;
using System.Web.Http.Results;
using Moq;
using NuGet.Server.Core.DataServices;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.Core.Tests;
using NuGet.Server.Core.Tests.Infrastructure;
using NuGet.Server.V2.Model;
using NuGet.Server.V2.Tests.Infrastructure;
using NuGet.Server.V2.Tests.TestUtils;
using Xunit;

namespace NuGet.Server.V2.Tests
{
    public class NuGetODataControllerTests
    {
        private static CancellationToken Token => CancellationToken.None;

        internal static ServerPackage CreatePackageWithDefaults(
            string id,
            string version,
            bool listed = true,
            string supportedFrameworks = null,
            IEnumerable<string> authors = null,
            IEnumerable<string> owners = null)
        {
            var serverPackage = new ServerPackage()
            {
                Id = id,
                Version = SemanticVersion.Parse(version),
                Listed = listed,
                SupportedFrameworks = supportedFrameworks,
                Authors = authors ?? Enumerable.Empty<string>(),
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
            [InlineData("Id eq 'Foo'", 100, new[] { "Foo", "Foo" }, new[] { "1.0.0", "1.0.1-a" })]
            [InlineData("Id eq 'Bar'", 1, new[] { "Bar" }, new[] { "1.0.0" })]
            [InlineData("Id eq 'Bar' and IsPrerelease eq true", 100, new[] { "Bar", "Bar" }, new[] { "2.0.1-a", "2.0.1-b" })]
            [InlineData("Id eq 'Bar' or Id eq 'Foo'", 100, new[] { "Foo", "Foo", "Bar", "Bar", "Bar", "Bar" }, new[] { "1.0.0", "1.0.1-a", "1.0.0", "2.0.0", "2.0.1-a", "2.0.1-b" })]
            public async Task PackagesReturnsCollection(string filter, int top, string[] expectedIds, string[] expectedVersions)
            {
                // Arrange
                var repo = ControllerTestHelpers.SetupTestPackageRepository();
                var v2Service = new TestableNuGetODataController(repo.Object);

                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Packages?$filter=" + filter + "&$top=" + top);

                // Act
                var result = (await v2Service.Get(
                        new ODataQueryOptions<ODataPackage>(
                            new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)),
                            v2Service.Request),
                        semVerLevel: "",
                        token: Token))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(expectedIds.Length, result.Length);
                Assert.Equal(expectedVersions.Length, result.Length);
                for (var i = 0; i < expectedIds.Length; i++)
                {
                    var expectedId = expectedIds[i];
                    var expectedVersion = expectedVersions[i];

                    Assert.True(result.Any(p => p.Id == expectedId && p.Version == expectedVersion), string.Format("Search results did not contain {0} {1}", expectedId, expectedVersion));
                }
            }

            [Theory]
            [InlineData("", "1.0.0")]
            [InlineData("1.0.0", "1.0.0")]
            [InlineData("2.0.0", "2.0.0")]
            public async Task PackagesCountSupportsSemVerLevel(string semVerLevel, string expected)
            {
                // Arrange
                var repo = ControllerTestHelpers.SetupTestPackageRepository();
                var v2Service = new TestableNuGetODataController(repo.Object);

                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Packages/$count");

                // Act
                var result = await v2Service.GetCount(
                        new ODataQueryOptions<ODataPackage>(
                            new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)),
                            v2Service.Request),
                        semVerLevel: semVerLevel,
                        token: Token);

                // Assert
                repo.Verify(x => x.GetPackagesAsync(
                    It.Is<ClientCompatibility>(c => c.SemVerLevel.Equals(new SemanticVersion(expected))),
                    It.IsAny<CancellationToken>()));
            }

            [Theory]
            [InlineData("Id eq 'Foo'", 100, 2)]
            [InlineData("Id eq 'Bar'", 1, 1)]
            [InlineData("Id eq 'Bar' and IsPrerelease eq true", 100, 2)]
            [InlineData("Id eq 'Bar' or Id eq 'Foo'", 100, 6)]
            [InlineData("Id eq 'NotBar'", 100, 0)]
            public async Task PackagesCountReturnsCorrectCount(string filter, int top, int expectedNumberOfPackages)
            {
                // Arrange
                var repo = ControllerTestHelpers.SetupTestPackageRepository();
                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Packages/$count?$filter=" + filter + "&$top=" + top);

                // Act
                var result = (await v2Service.GetCount(
                        new ODataQueryOptions<ODataPackage>(
                            new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)),
                            v2Service.Request),
                        semVerLevel: "",
                        token: Token))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectResult<PlainTextResult>();

                // Assert
                Assert.Equal(expectedNumberOfPackages.ToString(), result.Content);
            }

            [Theory]
            [InlineData("Foo", "1.0.0")]
            [InlineData("Foo", "1.0.1-a")]
            [InlineData("Bar", "1.0.0")]
            [InlineData("Bar", "2.0.0")]
            [InlineData("Bar", "2.0.1-b")]
            [InlineData("Baz", "2.0.1-b.1")]
            [InlineData("Baz", "2.0.2-b+git")]
            [InlineData("Baz", "2.0.2+git")]
            public async Task PackagesByIdAndVersionReturnsPackage(string expectedId, string expectedVersion)
            {
                // Arrange
                var repo = ControllerTestHelpers.SetupTestPackageRepository();
                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Packages(Id='" + expectedId + "', Version='" + expectedVersion + "')");

                // Act
                var result = (await v2Service.Get(
                        new ODataQueryOptions<ODataPackage>(
                            new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)),
                            v2Service.Request),
                        expectedId,
                        expectedVersion,
                        Token))
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
            [InlineData("NoBar", "1.0.0-a.1")]
            [InlineData("NoBar", "1.0.0-a.1+git")]
            [InlineData("NoBar", "1.0.0+git")]
            public async Task PackagesByIdAndVersionReturnsNotFoundWhenPackageNotFound(string expectedId, string expectedVersion)
            {
                // Arrange
                var repo = ControllerTestHelpers.SetupTestPackageRepository();

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Packages(Id='" + expectedId + "', Version='" + expectedVersion + "')");

                // Act
                (await v2Service.Get(
                        new ODataQueryOptions<ODataPackage>(
                            new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)),
                            v2Service.Request),
                        expectedId,
                        expectedVersion,
                        Token))
                    .ExpectResult<NotFoundResult>();
            }
        }

        public class DownloadMethod
        {
            [Theory]
            [InlineData("1.0.0")]
            [InlineData("1.0.0-a")]
            [InlineData("1.0.0-a.1")]
            [InlineData("1.0.0-a.1+git")]
            [InlineData("1.0.0-a+git")]
            [InlineData("1.0.0+git")]
            public async Task DownloadReturnsContentOfExistingPackage(string version)
            {
                // Arrange
                using (var temporaryDirectory = new TemporaryDirectory())
                {
                    var packageContent = "package";
                    var packagePath = Path.Combine(temporaryDirectory.Path, "test.nupkg");
                    File.WriteAllText(packagePath, packageContent);

                    var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                    repo.Setup(r => r.GetPackagesAsync(ClientCompatibility.Max, Token)).ReturnsAsync(
                        new[]
                        {
                            new ServerPackage
                                {
                                    Id = "Foo",
                                    Version = SemanticVersion.Parse(version),
                                    Listed = false,
                                    Authors = new [] { string.Empty },
                                    Owners = new [] { string.Empty },
                                    Description = string.Empty,
                                    Summary = string.Empty,
                                    Tags = string.Empty,
                                    FullPath = packagePath
                                }
                        }.AsQueryable());

                    var v2Service = new TestableNuGetODataController(repo.Object);
                    v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                    // Act
                    using (var result = await v2Service.Download("Foo", version))
                    {
                        // Assert
                        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
                        Assert.Equal("binary/octet-stream", result.Content.Headers.ContentType.ToString());
                        var actualContent = await result.Content.ReadAsStringAsync();
                        Assert.Equal(packageContent, actualContent);
                    }
                }
            }

            [Fact]
            public async Task DownloadReturnsNotFoundOnMissingPackage()
            {
                // Arrange
                using (var temporaryDirectory = new TemporaryDirectory())
                {
                    var packageContent = "package";
                    var packagePath = Path.Combine(temporaryDirectory.Path, "test.nupkg");
                    File.WriteAllText(packagePath, packageContent);

                    var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                    repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(),
                        It.IsAny<CancellationToken>())).ReturnsAsync(
                        new[]
                        {
                            new ServerPackage
                                {
                                    Id = "Foo",
                                    Version = SemanticVersion.Parse("1.0.0"),
                                    Listed = false,
                                    Authors = new [] { string.Empty },
                                    Owners = new [] { string.Empty },
                                    Description = string.Empty,
                                    Summary = string.Empty,
                                    Tags = string.Empty,
                                    FullPath = packagePath
                                }
                        }.AsQueryable());

                    var v2Service = new TestableNuGetODataController(repo.Object);
                    v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                    // Act
                    using (var result = await v2Service.Download("Bar", "1.0.0"))
                    {
                        // Assert
                        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
                    }
                }
            }
        }

        public class FindPackagesByIdMethod
        {
            [Fact]
            public async Task FindPackagesByIdReturnsUnlistedAndPrereleasePackages()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(),
                        It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.FindPackagesById(
                        new ODataQueryOptions<ODataPackage>(
                            new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)),
                            v2Service.Request),
                        "Foo",
                        semVerLevel: "",
                        token: Token))
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
            public async Task FindPackagesByIdExcludesSemVer2ByDefault()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.Is<ClientCompatibility>(x => x.SemVerLevel.Equals(new SemanticVersion("1.0.0"))),
                        It.IsAny<CancellationToken>())).ReturnsAsync(
                    new[]
                {
                        new ServerPackage
                            {
                                Id = "Foo",
                                Version = SemanticVersion.Parse("1.0.0"),
                                Listed = true,
                                Authors = new [] { string.Empty },
                                Owners = new [] { string.Empty },
                                Description = string.Empty,
                                Summary = string.Empty,
                                Tags = string.Empty
                            }
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (await v2Service.FindPackagesById(
                        new ODataQueryOptions<ODataPackage>(
                            new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)),
                            v2Service.Request),
                        "Foo",
                        semVerLevel: "",
                        token: Token))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(1, result.Count());
                Assert.Equal("Foo", result.First().Id);
                Assert.Equal("1.0.0", result.First().Version);
            }

            [Fact]
            public async Task FindPackagesByIdAllowsSemVer2ToBeIncluded()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.Is<ClientCompatibility>(x => x.SemVerLevel.Equals(new SemanticVersion("2.0.0"))),
                        It.IsAny<CancellationToken>())).ReturnsAsync(
                    new[]
                {
                        new ServerPackage
                            {
                                Id = "Foo",
                                Version = SemanticVersion.Parse("1.0.0"),
                                Listed = true,
                                Authors = new [] { string.Empty },
                                Owners = new [] { string.Empty },
                                Description = string.Empty,
                                Summary = string.Empty,
                                Tags = string.Empty
                            }
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (await v2Service.FindPackagesById(
                        new ODataQueryOptions<ODataPackage>(
                            new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)),
                            v2Service.Request),
                        "Foo",
                        semVerLevel: "2.0.0",
                        token: Token))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(1, result.Count());
                Assert.Equal("Foo", result.First().Id);
                Assert.Equal("1.0.0", result.First().Version);
            }

            [Fact]
            public async Task FindPackagesByIdReturnsEmptyCollectionWhenNoPackages()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo
                    .Setup(r => r.GetPackagesAsync(It.IsAny<ClientCompatibility>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Enumerable.Empty<IServerPackage>());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (await v2Service.FindPackagesById(
                        new ODataQueryOptions<ODataPackage>(
                            new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)),
                            v2Service.Request),
                        "Foo",
                        semVerLevel: "",
                        token: Token))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(0, result.Count());
            }

            [Fact]
            public async Task FindPackagesByIdDoesNotHitBackendWhenIdIsEmpty()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Loose);

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (await v2Service.FindPackagesById(
                        new ODataQueryOptions<ODataPackage>(
                            new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)),
                            v2Service.Request),
                        "",
                        semVerLevel: "",
                        token: Token))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                repo.Verify(r => r.GetPackagesAsync(
                    It.IsAny<ClientCompatibility>(),
                    It.IsAny<CancellationToken>()), Times.Never);
                Assert.Equal(0, result.Count());
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
            public async Task GetUpdatesReturnsEmptyResultsIfInputIsMalformed(string id, string version)
            {
                // Arrange
                var repo = Mock.Of<IServerPackageRepository>();
                var v2Service = new TestableNuGetODataController(repo);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (await v2Service.GetUpdates(
                        new ODataQueryOptions<ODataPackage>(
                            new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)),
                            v2Service.Request),
                        id,
                        version,
                        includePrerelease: false,
                        includeAllVersions: true,
                        targetFrameworks: null,
                        versionConstraints: null))
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Empty(result);
            }

            [Fact]
            public async Task GetUpdatesExcludesSemVer2ByDefault()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                    It.Is<ClientCompatibility>(x => x.SemVerLevel.Equals(new SemanticVersion("1.0.0"))),
                    It.IsAny<CancellationToken>())).ReturnsAsync(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true)
                }.AsQueryable());
                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo",
                    "1.0.0",
                    includePrerelease: false,
                    includeAllVersions: false,
                    targetFrameworks: null,
                    versionConstraints: null,
                    semVerLevel: ""))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(0, result.Count());
            }

            [Fact]
            public async Task GetUpdatesAllowsSemVer2ToBeIncluded()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                    It.Is<ClientCompatibility>(x => x.SemVerLevel.Equals(new SemanticVersion("2.0.0"))),
                    It.IsAny<CancellationToken>())).ReturnsAsync(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true)
                }.AsQueryable());
                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo",
                    "1.0.0",
                    includePrerelease: false,
                    includeAllVersions: false,
                    targetFrameworks: null,
                    versionConstraints: null,
                    semVerLevel: "2.0.0"))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(0, result.Count());
            }

            [Fact]
            public async Task GetUpdatesIgnoresItemsWithMalformedVersions()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                    It.IsAny<ClientCompatibility>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|abcd",
                    includePrerelease: false,
                    includeAllVersions: false,
                    targetFrameworks: null,
                    versionConstraints: null))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(1, result.Count());
                AssertPackage(new { Id = "Foo", Version = "1.2.0" }, result.First());
            }

            [Fact]
            public async Task GetUpdatesReturnsVersionsNewerThanListedVersion()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                    It.IsAny<ClientCompatibility>(),
                    It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: null))
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

            [Fact]
            public async Task GetUpdatesIgnoresPackagesNotRequested()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(),
                        It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo",
                    "1.0.0",
                    includePrerelease: false,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: null))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                    .ToArray();

                // Assert
                Assert.Equal(2, result.Length);
                AssertPackage(new { Id = "Foo", Version = "1.1.0" }, result[0]);
                AssertPackage(new { Id = "Foo", Version = "1.2.0" }, result[1]);
            }

            [Theory]
            [InlineData("2.3|3.5|(1.0,2.3)")]
            [InlineData("2.3")]
            [InlineData("1.0||2.0")]
            [InlineData("||")]
            [InlineData("|1.0|")]
            public async Task GetUpdatesReturnsEmptyIfVersionConstraintsContainWrongNumberOfElements(string constraintString)
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(), 
                        It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: constraintString))
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(0, result.Count());
            }

            [Fact]
            public async Task GetUpdatesReturnsVersionsConformingToConstraints()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(), 
                        It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: "(,1.2.0)|[2.0,2.3]"))
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
            public async Task GetUpdatesIgnoreInvalidVersionConstraints()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(), 
                        It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: "(,1.2.0)|abdfsdf"))
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
            public async Task GetUpdatesReturnsVersionsConformingToConstraintsWithMissingConstraintsForSomePackges()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(), 
                        It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: false,
                    targetFrameworks: null,
                    versionConstraints: "|(1.2,2.8)"))
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
            public async Task GetUpdatesReturnsEmptyPackagesIfNoPackageSatisfiesConstraints()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(), 
                        It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: false,
                    targetFrameworks: null,
                    versionConstraints: "3.4|4.0"))
                    .ExpectQueryResult<ODataPackage>()
                    .GetInnerResult()
                    .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>();

                // Assert
                Assert.Equal(0, result.Count());
            }

            [Fact]
            public async Task GetUpdatesReturnsCaseInsensitiveMatches()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(), 
                        It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "foo",
                    "1.0.0",
                    includePrerelease: false,
                    includeAllVersions: false,
                    targetFrameworks: null,
                    versionConstraints: null))
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
            public async Task GetUpdatesReturnsUpdateIfAnyOfTheProvidedVersionsIsOlder()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(),
                        It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Foo|Qux",
                    "1.0.0|1.2.0|0.9",
                    includePrerelease: false,
                    includeAllVersions: false,
                    targetFrameworks: null,
                    versionConstraints: null))
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
            public async Task GetUpdatesReturnsPrereleasePackages()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(), 
                        It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.1.0|0.9",
                    includePrerelease: true,
                    includeAllVersions: true,
                    targetFrameworks: null,
                    versionConstraints: null))
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
            public async Task GetUpdatesReturnsResultsIfDuplicatesInPackageList()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(), 
                        It.IsAny<CancellationToken>())).ReturnsAsync(
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
                var result = (await v2Service.GetUpdates(
                        new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                        "Foo|Qux|Foo",
                        "0.9|1.5|1.1.2",
                        includePrerelease: false,
                        includeAllVersions: true,
                        targetFrameworks: null,
                        versionConstraints: null))
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
            public async Task GetUpdatesFiltersByTargetFramework()
            {
                // Arrange
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(), 
                        It.IsAny<CancellationToken>())).ReturnsAsync(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true, supportedFrameworks: "SL5|Net40-Full"),
                        CreatePackageWithDefaults("Foo", "1.3.0-alpha", listed: true, supportedFrameworks: "SL5|Net40-Full"),
                        CreatePackageWithDefaults("Foo", "2.0.0", listed: true, supportedFrameworks: "SL5|WinRT"),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true),
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0|1.5",
                    includePrerelease: false,
                    includeAllVersions: true,
                    targetFrameworks: "net40",
                    versionConstraints: null))
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
            public async Task GetUpdatesFiltersIncludesHighestPrereleasePackage()
            {
                // Arrange
                var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
                repo.Setup(r => r.GetPackagesAsync(
                        It.IsAny<ClientCompatibility>(), 
                        It.IsAny<CancellationToken>())).ReturnsAsync(
                    new[]
                {
                        CreatePackageWithDefaults("Foo", "1.0.0", listed: true),
                        CreatePackageWithDefaults("Foo", "1.1.0", listed: true, supportedFrameworks: "SL5|Net40-Full"),
                        CreatePackageWithDefaults("Foo", "1.2.0", listed: true, supportedFrameworks: "SL5|Net40-Full"),
                        CreatePackageWithDefaults("Foo", "1.3.0-alpha", listed: true, supportedFrameworks: "SL5|Net40-Full"),
                        CreatePackageWithDefaults("Foo", "2.0.0", listed: true, supportedFrameworks: "SL5|WinRT"),
                        CreatePackageWithDefaults("Qux", "2.0", listed: true),
                }.AsQueryable());

                var v2Service = new TestableNuGetODataController(repo.Object);
                v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/");

                // Act
                var result = (await v2Service.GetUpdates(
                    new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                    "Foo|Qux",
                    "1.0|1.5",
                    includePrerelease: true,
                    includeAllVersions: false,
                    targetFrameworks: "net40",
                    versionConstraints: null))
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

        public class SearchMethod
        {
            [Theory]
            [InlineData("Foo", false, new[] { "Foo" }, new[] { "1.0.0" })]
            [InlineData("Bar", false, new[] { "Bar", "Bar" }, new[] { "1.0.0", "2.0.0" })]
            [InlineData("", false, new[] { "Foo", "Bar", "Bar" }, new[] { "1.0.0", "1.0.0", "2.0.0" })]
            [InlineData("CommonTag", false, new[] { "Foo", "Bar", "Bar" }, new[] { "1.0.0", "1.0.0", "2.0.0" })]
            [InlineData("CommonTag CommonTag2", false, new[] { "Foo", "Bar", "Bar" }, new[] { "1.0.0", "1.0.0", "2.0.0" })]
            [InlineData("", true, new[] { "Foo", "Foo", "Bar", "Bar", "Bar" }, new[] { "1.0.0", "1.0.1-a", "1.0.0", "2.0.0", "2.0.1-a" })]
            public async Task SearchFiltersPackagesBySearchTermAndPrereleaseFlag(string searchTerm, bool includePrerelease, string[] expectedIds, string[] expectedVersions)
            {
                using (var temporaryDirectory = new TemporaryDirectory())
                {
                    // Arrange
                    var serverRepository = await CreateRepositoryWithPackagesAsync(temporaryDirectory);

                    var v2Service = new TestableNuGetODataController(serverRepository);
                    v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Search()?searchTerm='" + searchTerm + "'&targetFramework=''&includePrerelease=" + includePrerelease);

                    // Act
                    var result = (await v2Service.Search(
                        new ODataQueryOptions<ODataPackage>(new ODataQueryContext(NuGetV2WebApiEnabler.BuildNuGetODataModel(), typeof(ODataPackage)), v2Service.Request),
                        searchTerm: searchTerm,
                        targetFramework: null,
                        includePrerelease: includePrerelease))
                        .ExpectQueryResult<ODataPackage>()
                        .GetInnerResult()
                        .ExpectOkNegotiatedContentResult<IQueryable<ODataPackage>>()
                        .ToArray();


                    // Assert
                    Assert.Equal(expectedIds.Length, result.Length);
                    Assert.Equal(expectedVersions.Length, result.Length);
                    for (var i = 0; i < expectedIds.Length; i++)
                    {
                        var expectedId = expectedIds[i];
                        var expectedVersion = expectedVersions[i];

                        Assert.True(result.Any(p => p.Id == expectedId && p.Version == expectedVersion), string.Format("Search results did not contain {0} {1}", expectedId, expectedVersion));
                    }
                }
            }

            [Theory]
            [InlineData("Foo", false, 1)]
            [InlineData("Bar", false, 2)]
            [InlineData("", false, 3)]
            [InlineData("CommonTag", false, 3)]
            [InlineData("", true, 5)]
            public async Task SearchCountFiltersPackagesBySearchTermAndPrereleaseFlag(string searchTerm, bool includePrerelease, int expectedNumberOfPackages)
            {
                using (var temporaryDirectory = new TemporaryDirectory())
                {
                    // Arrange
                    var serverRepository = await CreateRepositoryWithPackagesAsync(temporaryDirectory);

                    var v2Service = new TestableNuGetODataController(serverRepository);
                    v2Service.Request = new HttpRequestMessage(HttpMethod.Get, "https://localhost:8081/api/v2/Search()/$count?searchTerm='" + searchTerm + "'&targetFramework=''&includePrerelease=false");

                    // Act
                    var result = (await v2Service.SearchCount(
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
            }

            private async Task<ServerPackageRepository> CreateRepositoryWithPackagesAsync(TemporaryDirectory temporaryDirectory)
            {
                return await ServerPackageRepositoryTest.CreateServerPackageRepositoryAsync(temporaryDirectory.Path, repository =>
                {
                    repository.AddPackage(CreatePackage("Foo", "1.0.0", new[] { "Foo", "CommonTag", "CommonTag2" }));
                    repository.AddPackage(CreatePackage("Foo", "1.0.1-a", new[] { "Foo", "CommonTag", "CommonTag2" }));
                    repository.AddPackage(CreatePackage("Bar", "1.0.0", new[] { "Bar", "CommonTag", "CommonTag2" }));
                    repository.AddPackage(CreatePackage("Bar", "2.0.0", new[] { "Bar", "CommonTag", "CommonTag2" }));
                    repository.AddPackage(CreatePackage("Bar", "2.0.1-a", new[] { "Bar", "CommonTag", "CommonTag2" }));
                });
            }

            private IPackage CreatePackage(string id, string version, IEnumerable<string> tags)
            {
                var package = new PackageBuilder
                {
                    Id = id,
                    Version = new SemanticVersion(version),
                    Authors = { "Test" },
                    Owners = { "Test" },
                    Description = id,
                    Summary = id,
                };

                package.Tags.AddRange(tags);

                var mockFile = new Mock<IPackageFile>();
                mockFile.Setup(m => m.Path).Returns("foo");
                mockFile.Setup(m => m.GetStream()).Returns(new MemoryStream());
                package.Files.Add(mockFile.Object);

                var packageStream = new MemoryStream();
                package.Save(packageStream);
                packageStream.Seek(0, SeekOrigin.Begin);

                return new ZipPackage(packageStream);
            }
        }
    }
}
