// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
using System.Collections.Specialized;
using NuGet.Server.Infrastructure;
using Xunit;

namespace NuGet.Server.Tests
{
    public class PackageAuthenticationServiceTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("foo")]
        [InlineData("not-true")]
        public void AuthenticationServiceReturnsFalseIfRequireApiKeyValueIsMalformed(string requireApiKey)
        {
            // Arrange
            var collection = new NameValueCollection
            {
                { "requireApiKey", requireApiKey }
            };

            // Act
            var result = PackageAuthenticationService.IsAuthenticatedInternal("test-apikey", collection);

            // Assert
            Assert.False(result);
        }

        [Theory]
        [InlineData("false")]
        [InlineData("FaLse")]
        public void AuthenticationServiceReturnsTrueIfRequireApiKeyValueIsSetToFalse(string keyValue)
        {
            // Arrange
            var collection = new NameValueCollection
            {
                { "requireApiKey", keyValue },
                { "apiKey", "test-key" }
            };

            // Act
            var result = PackageAuthenticationService.IsAuthenticatedInternal("incorrect-key", collection);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("incorrect-key")]
        public void AuthenticationServiceReturnsFalseIfKeyDoesNotMatchConfigurationKey(string key)
        {
            // Arrange
            var collection = new NameValueCollection
            {
                { "requireApiKey", "true" },
                { "apiKey", "test-key" }
            };

            // Act
            var result = PackageAuthenticationService.IsAuthenticatedInternal(key, collection);

            // Assert
            Assert.False(result);
        }

        [Theory]
        [InlineData("test-key")]
        [InlineData("tEst-Key")]
        public void AuthenticationServiceReturnsTrueIfKeyMatchesConfigurationKey(string key)
        {
            // Arrange
            var collection = new NameValueCollection
            {
                { "requireApiKey", "true" },
                { "apiKey", "test-key" }
            };

            // Act
            var result = PackageAuthenticationService.IsAuthenticatedInternal(key, collection);

            // Assert
            Assert.True(result);
        }
    }
}
