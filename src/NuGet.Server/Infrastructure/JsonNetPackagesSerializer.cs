// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;

namespace NuGet.Server.Infrastructure
{
    public class JsonNetPackagesSerializer
        : IPackagesSerializer
    {
        private static readonly SemanticVersion CurrentSchemaVersion = new SemanticVersion("3.0.0");

        private readonly JsonSerializer _serializer = new JsonSerializer
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
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
    }
}