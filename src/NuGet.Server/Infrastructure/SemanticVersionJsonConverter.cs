// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using Newtonsoft.Json;

namespace NuGet.Server.Infrastructure
{
    public class SemanticVersionJsonConverter
        : JsonConverter
    {
        private readonly JsonSerializer _serializer = new JsonSerializer();

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(SemanticVersion)
                || objectType == typeof(Version);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var value = _serializer.Deserialize<string>(reader);

            if (!string.IsNullOrEmpty(value))
            {
                if (objectType == typeof(SemanticVersion))
                {
                    return SemanticVersion.Parse(value);
                }

                if (objectType == typeof(Version))
                {
                    return Version.Parse(value);
                }
            }

            return null;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            _serializer.Serialize(writer, value.ToString());
        }
    }
}