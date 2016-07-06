// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using NuGet.Server.Core.Infrastructure;
using Xunit;

namespace NuGet.Server.Core.Tests
{
    public class ApiKeyPackageAuthenticationServiceTest
    {
        [Theory]
        [InlineData(null, null)]
        [InlineData(null, "test-key")]
        [InlineData("incorrect-key",null)]
        [InlineData("incorrect-key", "test-key")]
        [InlineData("test-key", "test-key")]
        public void AuthenticationServiceReturnsTrueIfRequireApiKeyValueIsSetToFalse(string key, string apiKey)
        {
            var apiKeyAuthService = new ApiKeyPackageAuthenticationService(false, apiKey);

            // Act
            var result = apiKeyAuthService.IsAuthenticatedInternal(key);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("incorrect-key")]
        public void AuthenticationServiceReturnsFalseIfKeyDoesNotMatchConfigurationKey(string key)
        {
            var apiKeyAuthService = new ApiKeyPackageAuthenticationService(true, "test-key");

            // Act
            var result = apiKeyAuthService.IsAuthenticatedInternal(key);

            // Assert
            Assert.False(result);
        }


        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ConstructorThrowsWhenApiKeyRequiredAndMissing(string apiKey)
        {
            Assert.Throws<ArgumentException>(() => new ApiKeyPackageAuthenticationService(true, apiKey));
        }

        [Theory]
        [InlineData("test-key")]
        [InlineData("tEst-Key")]
        public void AuthenticationServiceReturnsTrueIfKeyMatchesConfigurationKey(string key)
        {
            var apiKeyAuthService = new ApiKeyPackageAuthenticationService(true, "test-key");

            // Act
            var result = apiKeyAuthService.IsAuthenticatedInternal(key);

            // Assert
            Assert.True(result);
        }


    }
}
