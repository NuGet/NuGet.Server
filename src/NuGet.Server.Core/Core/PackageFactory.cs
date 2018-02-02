// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.IO;

namespace NuGet.Server.Core
{
    public static class PackageFactory
    {
        public static IPackage Open(string fullPackagePath)
        {
            if (string.IsNullOrEmpty(fullPackagePath))
            {
                throw new ArgumentNullException(nameof(fullPackagePath));
            }

            var directoryName = Path.GetDirectoryName(fullPackagePath);
            var fileName = Path.GetFileName(fullPackagePath);

            var fileSystem = new PhysicalFileSystem(directoryName);

            return new OptimizedZipPackage(
                fileSystem,
                fileName,
                NullFileSystem.Instance);
        }
    }
}
