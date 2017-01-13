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
            var target = new PackageAuthenticationService(collection);

            // Act
            var result = target.IsAuthenticated(user: null, apiKey: "test-apikey", packageId: null);

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
            var target = new PackageAuthenticationService(collection);

            // Act
            var result = target.IsAuthenticated(user: null, apiKey: "incorrect-key", packageId: null);

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
            var target = new PackageAuthenticationService(collection);

            // Act
            var result = target.IsAuthenticated(user: null, apiKey: key, packageId: null);

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
            var target = new PackageAuthenticationService(collection);

            // Act
            var result = target.IsAuthenticated(user: null, apiKey: key, packageId: null);

            // Assert
            Assert.True(result);
        }
    }
}
