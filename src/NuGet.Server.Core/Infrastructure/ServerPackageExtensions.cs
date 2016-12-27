// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;

namespace NuGet.Server.Core.Infrastructure
{
    public static class ServerPackageExtensions
    {
        public static IEnumerable<T> FilterByPrerelease<T>(this IEnumerable<T> packages, bool allowPrerelease)
            where T : IPackage
        {
            if (packages == null)
            {
                return null;
            }

            if (!allowPrerelease)
            {
                packages = packages.Where(p => p.IsReleaseVersion());
            }

            return packages;
        }
    }
}
