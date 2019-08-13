// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;

namespace NuGet.Server.Core.Infrastructure
{
    public class JsonNetPackagesSerializer
        : IPackagesSerializer
    {
        private static readonly SemanticVersion CurrentSchemaVersion = new SemanticVersion("3.0.0");

        private readonly JsonSerializer _serializer = new JsonSerializer
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new AbsoluteUriConverter() },
        };

        public void Serialize(IEnumerable<ServerPackage> packages, Stream stream)
        {
            using (var writer = new JsonTextWriter(new StreamWriter(stream, Encoding.UTF8, 1024, true)))
            {
                _serializer.Serialize(
                    writer,
                    new SerializedServerPackages
                    {
                        SchemaVersion = CurrentSchemaVersion,
                        Packages = packages.ToList()
                    });
            }
        }

        public IEnumerable<ServerPackage> Deserialize(Stream stream)
        {
            using (var reader = new JsonTextReader(new StreamReader(stream, Encoding.UTF8, false, 1024, true)))
            {
                var packages = _serializer.Deserialize<SerializedServerPackages>(reader);

                if (packages == null || packages.SchemaVersion != CurrentSchemaVersion)
                {
                    throw new SerializationException(
                        $"The expected schema version of the packages file is '{CurrentSchemaVersion}', not " +
                        $"'{packages?.SchemaVersion}'.");
                }
                
                return packages.Packages;
            }
        }

        /// <summary>
        /// This is necessary because Newtonsoft.Json creates <see cref="Uri"/> instances with
        /// <see cref="UriKind.RelativeOrAbsolute"/> which treats UNC paths as relative. NuGet.Core uses
        /// <see cref="UriKind.Absolute"/> which treats UNC paths as absolute. For more details, see:
        /// https://github.com/JamesNK/Newtonsoft.Json/issues/2128
        /// </summary>
        private class AbsoluteUriConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Uri);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null)
                {
                    return null;
                }
                else if (reader.TokenType == JsonToken.String)
                {
                    return new Uri((string)reader.Value, UriKind.Absolute);
                }

                throw new JsonSerializationException("The JSON value must be a string.");
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value == null)
                {
                    writer.WriteNull();
                    return;
                }

                if (!(value is Uri uriValue))
                {
                    throw new JsonSerializationException("The value must be a URI.");
                }

                if (!uriValue.IsAbsoluteUri)
                {
                    throw new JsonSerializationException("The URI value must be an absolute Uri. Relative URI instances are not allowed.");
                }

                writer.WriteValue(uriValue.OriginalString);
            }
        }
    }
}