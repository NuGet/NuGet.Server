// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Reflection;

namespace NuGet.Server.Core.Tests
{
    public static class TestData
    {
        public const string PackageResource = "NuGet.Core.2.12.0.nupkg";
        public const string PackageId = "NuGet.Core";
        public const string PackageVersionString = "2.12.0";
        public static readonly SemanticVersion PackageVersion = new SemanticVersion(PackageVersionString);

        public static Stream GetResourceStream(string name)
        {
            var assembly = Assembly.GetExecutingAssembly();
            return assembly.GetManifestResourceStream($"NuGet.Server.Core.Tests.TestData.{name}");
        }

        public static string GetResourceString(string name)
        {
            using (var stream = GetResourceStream(name))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        public static void CopyResourceToPath(string name, string path)
        {
            using (var resourceStream = GetResourceStream(name))
            using (var outputStream = File.Create(path))
            {
                resourceStream.CopyTo(outputStream);
            }
        }
    }
}
