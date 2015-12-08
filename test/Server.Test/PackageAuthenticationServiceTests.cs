using System.Collections.Specialized;
using Xunit;
using Xunit.Extensions;

namespace NuGet.Server.Infrastructure.Test
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
            var collection = new NameValueCollection();
            collection.Add("requireApiKey", requireApiKey);

            // Act
            bool result = PackageAuthenticationService.IsAuthenticatedInternal("test-apikey", collection);

            // Assert
            Assert.False(result);
        }

        [Theory]
        [InlineData("false")]
        [InlineData("FaLse")]
        public void AuthenticationServiceReturnsTrueIfRequireApiKeyValueIsSetToFalse(string keyValue)
        {
            // Arrange
            var collection = new NameValueCollection();
            collection.Add("requireApiKey", keyValue);
            collection.Add("apiKey", "test-key");

            // Act
            bool result = PackageAuthenticationService.IsAuthenticatedInternal("incorrect-key", collection);

            // Assert
            Assert.True(result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("incorrect-key")]
        public void AuthenticationServiceReturnsFalseIfKeyDoesNotMatchConfigurationKey(string key)
        {
            // Arrange
            var collection = new NameValueCollection();
            collection.Add("requireApiKey", "true");
            collection.Add("apiKey", "test-key");

            // Act
            bool result = PackageAuthenticationService.IsAuthenticatedInternal(key, collection);

            // Assert
            Assert.False(result);
        }

        [Theory]
        [InlineData("test-key")]
        [InlineData("tEst-Key")]
        public void AuthenticationServiceReturnsTrueIfKeyMatchesConfigurationKey(string key)
        {
            // Arrange
            var collection = new NameValueCollection();
            collection.Add("requireApiKey", "true");
            collection.Add("apiKey", "test-key");

            // Act
            bool result = PackageAuthenticationService.IsAuthenticatedInternal(key, collection);

            // Assert
            Assert.True(result);
        }
    }
}
