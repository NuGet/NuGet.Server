// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NuGet.Server.Core.Infrastructure;
using Xunit;

namespace NuGet.Server.V2.Tests
{
    public class UploadPackageTests
    {
        private const string ApiKeyHeader = "X-NUGET-APIKEY";
        private static CancellationToken Token => CancellationToken.None;

        [Fact]
        public async Task UploadPackage_NoAuthService_Returns403()
        {
            // Arrange - controller with no auth service (null) means uploads are disabled
            var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
            var controller = new TestableNuGetODataController(repo.Object);
            controller.Request = new HttpRequestMessage(HttpMethod.Put, "https://localhost/nuget");
            controller.Request.Content = new ByteArrayContent(new byte[] { 0 });

            // Act
            var result = await controller.UploadPackage(Token);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
            // Repository should never be touched (MockBehavior.Strict ensures this)
        }

        [Fact]
        public async Task UploadPackage_InvalidApiKey_Returns403BeforeProcessingPackage()
        {
            // Arrange
            var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
            var authService = new Mock<IPackageAuthenticationService>(MockBehavior.Strict);

            // The early auth check is called with null packageId and returns false
            authService
                .Setup(a => a.IsAuthenticated(It.IsAny<IPrincipal>(), "bad-key", null))
                .Returns(false);

            var controller = new TestableNuGetODataController(repo.Object, authService.Object);
            controller.Request = new HttpRequestMessage(HttpMethod.Put, "https://localhost/nuget");
            controller.Request.Headers.Add(ApiKeyHeader, "bad-key");
            controller.Request.Content = new ByteArrayContent(new byte[] { 0 });

            // Act
            var result = await controller.UploadPackage(Token);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);

            // Auth was called exactly once — with null packageId (the early check).
            // If it were called a second time with a real packageId, MockBehavior.Strict
            // would throw because only the null-packageId setup exists.
            authService.Verify(
                a => a.IsAuthenticated(It.IsAny<IPrincipal>(), "bad-key", null),
                Times.Once);
        }

        [Fact]
        public async Task UploadPackage_NoApiKey_Returns403BeforeProcessingPackage()
        {
            // Arrange
            var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
            var authService = new Mock<IPackageAuthenticationService>(MockBehavior.Strict);

            // The early auth check with null apiKey should return false
            authService
                .Setup(a => a.IsAuthenticated(It.IsAny<IPrincipal>(), null, null))
                .Returns(false);

            var controller = new TestableNuGetODataController(repo.Object, authService.Object);
            controller.Request = new HttpRequestMessage(HttpMethod.Put, "https://localhost/nuget");
            // No API key header set
            controller.Request.Content = new ByteArrayContent(new byte[] { 0 });

            // Act
            var result = await controller.UploadPackage(Token);

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);

            authService.Verify(
                a => a.IsAuthenticated(It.IsAny<IPrincipal>(), null, null),
                Times.Once);
        }

        [Fact]
        public void ApiKeyPackageAuthenticationService_NullPackageId_ValidKey_ReturnsTrue()
        {
            var service = new ApiKeyPackageAuthenticationService(true, "my-secret-key");
            Assert.True(service.IsAuthenticated(null, "my-secret-key", null));
        }

        [Fact]
        public void ApiKeyPackageAuthenticationService_NullPackageId_InvalidKey_ReturnsFalse()
        {
            var service = new ApiKeyPackageAuthenticationService(true, "my-secret-key");
            Assert.False(service.IsAuthenticated(null, "wrong-key", null));
        }

        [Fact]
        public void ApiKeyPackageAuthenticationService_NullPackageId_NullKey_ReturnsFalse()
        {
            var service = new ApiKeyPackageAuthenticationService(true, "my-secret-key");
            Assert.False(service.IsAuthenticated(null, null, null));
        }

        [Fact]
        public void ApiKeyPackageAuthenticationService_NullPackageId_NoKeyRequired_ReturnsTrue()
        {
            var service = new ApiKeyPackageAuthenticationService(false, null);
            Assert.True(service.IsAuthenticated(null, null, null));
        }

        [Fact]
        public void ApiKeyPackageAuthenticationService_NullPackageId_ConsistentWithRealPackageId()
        {
            var service = new ApiKeyPackageAuthenticationService(true, "my-key");

            // Passing null packageId should give the same result as a real packageId
            Assert.Equal(
                service.IsAuthenticated(null, "my-key", "SomePackage"),
                service.IsAuthenticated(null, "my-key", null));

            Assert.Equal(
                service.IsAuthenticated(null, "bad-key", "SomePackage"),
                service.IsAuthenticated(null, "bad-key", null));
        }
    }
}
