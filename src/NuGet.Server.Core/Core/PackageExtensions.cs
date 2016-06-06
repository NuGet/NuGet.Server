// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.IO;
using System.Linq;

namespace NuGet.Server
{
    public static class PackageExtensions
    {
        public static bool IsSymbolsPackage(this IPackage package)
        {
            var hasSymbols = package.GetFiles()
                .Any(pf => string.Equals(Path.GetExtension(pf.Path), ".pdb", StringComparison.InvariantCultureIgnoreCase));

            return hasSymbols && package.GetFiles()
                   .Any(pf => pf.Path.StartsWith("src") || pf.Path.StartsWith("/src"));
        }
    }
}