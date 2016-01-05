// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using NuGet.Server.Infrastructure;
using Xunit;

namespace NuGet.Server.Tests
{
    public class SemanticVersionJsonConverterTests
    {
        [Fact]
        public void CanConvertSemanticVersion()
        {
            // Arrange
            var converter = new SemanticVersionJsonConverter();

            // Act and assert
            Assert.True(converter.CanConvert(typeof(SemanticVersion)));
        }

        [Fact]
        public void CanConvertVersion()
        {
            // Arrange
            var converter = new SemanticVersionJsonConverter();

            // Act and assert
            Assert.True(converter.CanConvert(typeof(Version)));
        }

        [Theory]
        [InlineData("1.0.0")]
        [InlineData("2.0.0.0")]
        [InlineData("3.0.0-alpha1")]
        public void SerializesSemanticVersionAsString(string version)
        {
            // Arrange
            var json = new StringBuilder();
            using (var writer = new JsonTextWriter(new StringWriter(json)))
            {
                var converter = new SemanticVersionJsonConverter();

                // Act
                converter.WriteJson(writer, SemanticVersion.Parse(version), new JsonSerializer());

                // Assert
                Assert.Equal("\"" + version + "\"", json.ToString());
            }
        }

        [Theory]
        [InlineData("1.0.0")]
        [InlineData("2.0.0.0")]
        public void SerializesVersionAsString(string version)
        {
            // Arrange
            var json = new StringBuilder();
            using (var writer = new JsonTextWriter(new StringWriter(json)))
            {
                var converter = new SemanticVersionJsonConverter();

                // Act
                converter.WriteJson(writer, Version.Parse(version), new JsonSerializer());

                // Assert
                Assert.Equal("\"" + version + "\"", json.ToString());
            }
        }

        [Theory]
        [InlineData("1.0.0", typeof(SemanticVersion))]
        [InlineData("2.0.0.0", typeof(SemanticVersion))]
        [InlineData("3.0.0-alpha", typeof(SemanticVersion))]
        [InlineData("1.0.0", typeof(Version))]
        [InlineData("2.0.0.0", typeof(Version))]
        public void Deserializes(string version, Type type)
        {
            // Arrange
            using (var reader = new JsonTextReader(new StringReader("\"" + version + "\"")))
            {
                var converter = new SemanticVersionJsonConverter();

                // Act
                var result = converter.ReadJson(reader, type, null, new JsonSerializer());

                // Assert
                Assert.Equal(version, result.ToString());
            }
        }
    }
}