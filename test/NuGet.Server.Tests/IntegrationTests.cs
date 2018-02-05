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
using NuGet.Server.Core.Tests;
using NuGet.Server.Core.Tests.Infrastructure;
using Xunit;
using ISystemDependencyResolver = System.Web.Http.Dependencies.IDependencyResolver;
using SystemHttpClient = System.Net.Http.HttpClient;

namespace NuGet.Server.Tests
{
    public class IntegrationTests
    {
        [Fact]
        public async Task DropPackageThenReadPackages()
        {
            // Arrange
            using (var tc = new TestContext())
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
            using (var tc = new TestContext())
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
            using (var tc = new TestContext())
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
            using (var tc = new TestContext())
            {
                string apiKey = "foobar";
                tc.SetApiKey(apiKey);

                var packagePath = Path.Combine(tc.TemporaryDirectory, "package.nupkg");
                TestData.CopyResourceToPath(TestData.PackageResource, packagePath);

                // Act & Assert
                // 1. Push the package.
                using (var request = new HttpRequestMessage(HttpMethod.Put, "/nuget")
                {
                    Headers =
                    {
                        { "X-NUGET-APIKEY", apiKey }
                    },
                    Content = tc.GetFileUploadContent(packagePath)
                })
                {
                    using (request)
                    using (var response = await tc.Client.SendAsync(request))
                    {
                        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                    }
                }

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

        [Theory]
        [MemberData(nameof(EndpointsSupportingProjection))]
        public async Task CanQueryUsingProjection(string endpoint)
        {
            // Arrange
            using (var tc = new TestContext())
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

            using (var tc = new TestContext())
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
            private readonly HttpConfiguration _config;

            public TestContext()
            {
                TemporaryDirectory = new TemporaryDirectory();
                PackagesDirectory = new TemporaryDirectory();

                Settings = new NameValueCollection
                {
                    { "requireApiKey", "true" },
                    { "apiKey", string.Empty }
                };

                ServiceResolver = new DefaultServiceResolver(PackagesDirectory, Settings);

                _config = new HttpConfiguration();
                _config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;
                _config.DependencyResolver = new DependencyResolverAdapter(ServiceResolver);

                NuGetODataConfig.Initialize(_config, "TestablePackagesOData");

                _server = new HttpServer(_config);
                Client = new SystemHttpClient(_server);
                Client.BaseAddress = new Uri("http://localhost/");
            }

            public DefaultServiceResolver ServiceResolver { get; }
            public TemporaryDirectory TemporaryDirectory { get; }
            public TemporaryDirectory PackagesDirectory { get; }
            public NameValueCollection Settings { get; }
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

            public void Dispose()
            {
                Client.Dispose();
                _server.Dispose();
                _config.Dispose();
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
