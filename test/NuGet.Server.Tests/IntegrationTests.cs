// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Dependencies;
using NuGet.Server.App_Start;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.Core.Logging;
using NuGet.Server.Core.Tests;
using NuGet.Server.Core.Tests.Infrastructure;
using NuGet.Server.V2;
using Xunit;
using Xunit.Abstractions;
using ISystemDependencyResolver = System.Web.Http.Dependencies.IDependencyResolver;
using SystemHttpClient = System.Net.Http.HttpClient;

namespace NuGet.Server.Tests
{
    public class IntegrationTests
    {
        private readonly ITestOutputHelper _output;

        public IntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task DropPackageThenReadPackages()
        {
            // Arrange
            using (var tc = new TestContext(_output))
            {
                // Act & Assert
                // 1. Get the initial list of packages. This should be empty.
                using (var request = new HttpRequestMessage(HttpMethod.Get, "/nuget/Packages()"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var content = await response.Content.ReadAsStringAsync();

                    Assert.DoesNotContain(TestData.PackageId, content);
                }

                // 2. Write a package to the drop folder.
                var packagePath = Path.Combine(tc.PackagesDirectory, "package.nupkg");
                TestData.CopyResourceToPath(TestData.PackageResource, packagePath);
                
                // 3. Get the list of packages again. This should have the added package.
                using (var request = new HttpRequestMessage(HttpMethod.Get, "/nuget/Packages()"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var content = await response.Content.ReadAsStringAsync();

                    Assert.Contains(TestData.PackageId, content);
                }
            }
        }

        [Fact]
        public async Task DownloadPackage()
        {
            // Arrange
            using (var tc = new TestContext(_output))
            {
                // Act & Assert
                // 1. Write a package to the drop folder.
                var packagePath = Path.Combine(tc.PackagesDirectory, "package.nupkg");
                TestData.CopyResourceToPath(TestData.PackageResource, packagePath);
                var expectedBytes = File.ReadAllBytes(packagePath);

                // 2. Download the package.
                using (var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/nuget/Packages(Id='{TestData.PackageId}',Version='{TestData.PackageVersionString}')/Download"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var actualBytes = await response.Content.ReadAsByteArrayAsync();

                    Assert.Equal("binary/octet-stream", response.Content.Headers.ContentType.ToString());
                    Assert.Equal(expectedBytes, actualBytes);
                }
            }
        }

        [Fact]
        public async Task FilterOnFramework()
        {
            // Arrange
            using (var tc = new TestContext(_output))
            {
                tc.Settings["enableFrameworkFiltering"] = "true";

                // Act & Assert
                // 1. Write a package to the drop folder.
                var packagePath = Path.Combine(tc.PackagesDirectory, "package.nupkg");
                TestData.CopyResourceToPath(TestData.PackageResource, packagePath);

                // 2. Search for all packages supporting .NET Framework 4.6 (this should match the test package)
                using (var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/nuget/Search?targetFramework='net46'"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var content = await response.Content.ReadAsStringAsync();

                    Assert.Contains(TestData.PackageId, content);
                }

                // 3. Search for all packages supporting .NET Framework 2.0 (this should match nothing)
                using (var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/nuget/Search?targetFramework='net20'"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var content = await response.Content.ReadAsStringAsync();

                    Assert.DoesNotContain(TestData.PackageId, content);
                }
            }
        }

        [Fact]
        public async Task PushPackageThenReadPackages()
        {
            // Arrange
            using (var tc = new TestContext(_output))
            {
                string apiKey = "foobar";
                tc.SetApiKey(apiKey);

                var packagePath = Path.Combine(tc.TemporaryDirectory, "package.nupkg");
                TestData.CopyResourceToPath(TestData.PackageResource, packagePath);

                // Act & Assert
                // 1. Push the package.
                await tc.PushPackageAsync(apiKey, packagePath);

                // 2. Get the list of packages. This should mention the package.
                using (var request = new HttpRequestMessage(HttpMethod.Get, "/nuget/Packages()"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var content = await response.Content.ReadAsStringAsync();

                    Assert.Contains(TestData.PackageId, content);
                }
            }
        }

        [Fact]
        public async Task CanSupportMultipleSetsOfRoutes()
        {
            // Arrange
            using (var tc = new TestContext(_output))
            {
                // Enable another set of routes.
                NuGetV2WebApiEnabler.UseNuGetV2WebApiFeed(
                    tc.Config,
                    "NuGetDefault2",
                    "nuget2",
                    TestablePackagesODataController.Name);

                string apiKey = "foobar";
                tc.SetApiKey(apiKey);

                var packagePath = Path.Combine(tc.TemporaryDirectory, "package.nupkg");
                TestData.CopyResourceToPath(TestData.PackageResource, packagePath);

                // Act & Assert
                // 1. Push to the legacy route.
                await tc.PushPackageAsync(apiKey, packagePath, "/api/v2/package");

                // 2. Make a request to the first set of routes.
                using (var request = new HttpRequestMessage(HttpMethod.Get, "/nuget/Packages()"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var content = await response.Content.ReadAsStringAsync();

                    Assert.Contains(TestData.PackageId, content);
                }

                // 3. Make a request to the second set of routes.
                using (var request = new HttpRequestMessage(HttpMethod.Get, "/nuget2/Packages()"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var content = await response.Content.ReadAsStringAsync();

                    Assert.Contains(TestData.PackageId, content);
                }
            }
        }

        /// <summary>
        /// Added due to https://github.com/NuGet/NuGetGallery/issues/6960. There was a concurrency issue when pushing
        /// packages that could lead to unnecessary cache rebuilds.
        /// </summary>
        [Fact]
        public async Task DoesNotRebuildTheCacheWhenPackagesArePushed()
        {
            // Arrange
            using (var tc = new TestContext(_output))
            {
                // Essentially disable the automatic cache rebuild by setting it really far in the future.
                const int initialCacheRebuildAfterSeconds = 60 * 60 * 24;
                tc.Settings["initialCacheRebuildAfterSeconds"] = initialCacheRebuildAfterSeconds.ToString();

                const int workerCount = 8;
                const int totalPackages = 320;

                var packagePaths = new ConcurrentBag<string>();
                for (var i = 0; i < totalPackages; i++)
                {
                    var packageId = Guid.NewGuid().ToString();
                    var packagePath = Path.Combine(tc.TemporaryDirectory, $"{packageId}.1.0.0.nupkg");
                    using (var package = TestData.GenerateSimplePackage(packageId, SemanticVersion.Parse("1.0.0")))
                    using (var fileStream = File.OpenWrite(packagePath))
                    {
                        await package.CopyToAsync(fileStream);
                    }

                    packagePaths.Add(packagePath);
                }

                string apiKey = "foobar";
                tc.SetApiKey(apiKey);

                // Act & Assert
                // 1. Push a single package to build the cache for the first time.
                packagePaths.TryTake(out var firstPackagePath);
                await tc.PushPackageAsync(apiKey, firstPackagePath);

                Assert.Single(tc.Logger.Messages, "[INFO] Start rebuilding package store...");
                tc.Logger.Clear();
                tc.TestOutputHelper.WriteLine("The first package has been pushed.");

                // 2. Execute a query to register the file system watcher.
                using (var request = new HttpRequestMessage(HttpMethod.Get, "/nuget/Packages/$count"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var content = await response.Content.ReadAsStringAsync();

                    Assert.Equal(1, int.Parse(content));
                }

                Assert.DoesNotContain("[INFO] Start rebuilding package store...", tc.Logger.Messages);
                tc.TestOutputHelper.WriteLine("The first count query has completed.");

                // 3. Push the rest of the packages.
                var workerTasks = Enumerable
                    .Range(0, workerCount)
                    .Select(async i =>
                    {
                        while (packagePaths.TryTake(out var packagePath))
                        {
                            await tc.PushPackageAsync(apiKey, packagePath);
                        }
                    })
                    .ToList();
                await Task.WhenAll(workerTasks);

                tc.TestOutputHelper.WriteLine("The rest of the packages have been pushed.");

                // 4. Get the total count of packages. This should match the number of packages pushed.
                using (var request = new HttpRequestMessage(HttpMethod.Get, "/nuget/Packages/$count"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var content = await response.Content.ReadAsStringAsync();

                    Assert.Equal(totalPackages, int.Parse(content));
                }

                Assert.DoesNotContain("[INFO] Start rebuilding package store...", tc.Logger.Messages);
                tc.TestOutputHelper.WriteLine("The second count query has completed.");
            }
        }

        [Theory]
        [MemberData(nameof(EndpointsSupportingProjection))]
        public async Task CanQueryUsingProjection(string endpoint)
        {
            // Arrange
            using (var tc = new TestContext(_output))
            {
                var packagePath = Path.Combine(tc.PackagesDirectory, "package.nupkg");
                TestData.CopyResourceToPath(TestData.PackageResource, packagePath);

                // Act
                using (var request = new HttpRequestMessage(HttpMethod.Get, $"/nuget/{endpoint}$select=Id,Version"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var content = await response.Content.ReadAsStringAsync();

                    // Assert
                    Assert.Contains(TestData.PackageId, content);
                    Assert.Contains(TestData.PackageVersionString, content);
                }
            }
        }

        [Fact]
        public async Task DoesNotWriteToNuGetScratch()
        {
            // Arrange
            OptimizedZipPackage.PurgeCache();
            var expectedTempEntries = Directory
                .GetFileSystemEntries(Path.Combine(Path.GetTempPath(), "NuGetScratch"))
                .OrderBy(x => x)
                .ToList();

            using (var tc = new TestContext(_output))
            {
                tc.Settings["enableFrameworkFiltering"] = "true";
                tc.Settings["allowOverrideExistingPackageOnPush"] = "true";

                string apiKey = "foobar";
                tc.SetApiKey(apiKey);

                // Act & Assert
                // 1. Write a package to the drop folder.
                var packagePath = Path.Combine(tc.PackagesDirectory, "package.nupkg");
                TestData.CopyResourceToPath(TestData.PackageResource, packagePath);

                // 2. Search for packages.
                using (var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/nuget/Search?targetFramework='net46'"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }

                // 3. Push the package.
                var pushPath = Path.Combine(tc.TemporaryDirectory, "package.nupkg");
                TestData.CopyResourceToPath(TestData.PackageResource, pushPath);
                using (var request = new HttpRequestMessage(HttpMethod.Put, "/nuget")
                {
                    Headers =
                    {
                        { "X-NUGET-APIKEY", apiKey }
                    },
                    Content = tc.GetFileUploadContent(pushPath),
                })
                {
                    using (request)
                    using (var response = await tc.Client.SendAsync(request))
                    {
                        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                    }
                }

                // 4. Search for packages again.
                using (var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/nuget/Search?targetFramework='net46'"))
                using (var response = await tc.Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }

                // 6. Make sure we have not added more temp files.
                var actualTempEntries = Directory
                    .GetFileSystemEntries(Path.Combine(Path.GetTempPath(), "NuGetScratch"))
                    .OrderBy(x => x)
                    .ToList();
                Assert.Equal(expectedTempEntries, actualTempEntries);
            }
        }

        public static IEnumerable<object[]> EndpointsSupportingProjection
        {
            get
            {
                yield return new object[] { "Packages()?" };
                yield return new object[] { "Search()?searchTerm=''&targetFramework=''&includePrerelease=true&includeDelisted=true&" };
                yield return new object[] { $"FindPackagesById()?id='{TestData.PackageId}'&" };
            }
        }

        private sealed class TestContext : IDisposable
        {
            private readonly HttpServer _server;

            public TestContext(ITestOutputHelper output)
            {
                TestOutputHelper = output;
                Logger = new TestOutputLogger(output);
                TemporaryDirectory = new TemporaryDirectory();
                PackagesDirectory = new TemporaryDirectory();

                Settings = new NameValueCollection
                {
                    { "requireApiKey", "true" },
                    { "apiKey", string.Empty }
                };

                ServiceResolver = new DefaultServiceResolver(PackagesDirectory, Settings, Logger);

                Config = new HttpConfiguration();
                Config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;
                Config.DependencyResolver = new DependencyResolverAdapter(ServiceResolver);

                NuGetODataConfig.Initialize(Config, TestablePackagesODataController.Name);

                _server = new HttpServer(Config);
                Client = new SystemHttpClient(_server);
                Client.BaseAddress = new Uri("http://localhost/");
            }

            public ITestOutputHelper TestOutputHelper { get; }
            public TestOutputLogger Logger { get; }
            public DefaultServiceResolver ServiceResolver { get; }
            public TemporaryDirectory TemporaryDirectory { get; }
            public TemporaryDirectory PackagesDirectory { get; }
            public NameValueCollection Settings { get; }
            public HttpConfiguration Config { get; }
            public SystemHttpClient Client { get; }

            public void SetApiKey(string apiKey)
            {
                Settings["apiKey"] = apiKey;
            }

            public MultipartContent GetFileUploadContent(params string[] paths)
            {
                var content = new MultipartContent();
                foreach (var path in paths)
                {
                    var fileName = Path.GetFileName(path);
                    content.Add(new StreamContent(File.OpenRead(path))
                    {
                        Headers =
                        {
                            ContentDisposition = new ContentDispositionHeaderValue("attachment")
                            {
                                FileNameStar = fileName
                            }
                        }
                    });
                }

                return content;
            }

            public async Task PushPackageAsync(string apiKey, string packagePath, string pushUrl = "/nuget")
            {
                using (var request = new HttpRequestMessage(HttpMethod.Put, pushUrl)
                {
                    Headers =
                    {
                        { "X-NUGET-APIKEY", apiKey }
                    },
                    Content = GetFileUploadContent(packagePath)
                })
                using (var response = await Client.SendAsync(request))
                {
                    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                }
            }

            public void Dispose()
            {
                Client.Dispose();
                _server.Dispose();
                Config.Dispose();
                ServiceResolver.Dispose();
                PackagesDirectory.Dispose();
                TemporaryDirectory.Dispose();
            }
        }

        private class DependencyResolverAdapter : ISystemDependencyResolver
        {
            private readonly IServiceResolver _resolver;
            private readonly ConcurrentBag<IDisposable> _disposables = new ConcurrentBag<IDisposable>();

            public DependencyResolverAdapter(IServiceResolver resolver)
            {
                _resolver = resolver;
            }

            public IDependencyScope BeginScope()
            {
                // This is sufficient for integration testing, but it not the "right" way to do it.
                return this;
            }

            public void Dispose()
            {
                IDisposable disposable;
                while (_disposables.TryTake(out disposable))
                {
                    disposable.Dispose();
                }
            }

            public object GetService(Type serviceType)
            {
                object instance = null;
                if (serviceType == typeof(TestablePackagesODataController))
                {
                    instance = new TestablePackagesODataController(_resolver);
                }

                var disposable = instance as IDisposable;
                if (instance != null)
                {
                    _disposables.Add(disposable);
                }

                return instance;
            }

            public IEnumerable<object> GetServices(Type serviceType)
            {
                var service = _resolver.Resolve(serviceType);

                if (service != null)
                {
                    yield return service;
                }                
            }
        }
    }
}
