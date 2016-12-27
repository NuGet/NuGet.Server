// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace NuGet.Server.Core.Infrastructure
{
    public static class ServerPackageExtensions
    {
        public static bool IsReleaseVersion(this IServerPackage package)
        {
            return string.IsNullOrEmpty(package.Version.SpecialVersion);
        }

        public static IEnumerable<T> FilterByPrerelease<T>(this IEnumerable<T> packages, bool allowPrerelease)
            where T : IServerPackage
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
        
        public static IQueryable<T> Find<T>(this IQueryable<T> packages, string searchText)
            where T : IServerPackage
        {
            var terms = searchText
                .Split()
                .Where(t => t.Length > 0)
                .ToList();

            return packages
                .Where(p => MatchesTerm(p, terms));
        }
        
        private static bool MatchesTerm(IServerPackage package, List<string> terms)
        {
            foreach (var term in terms)
            {
                if (IsNotNullAndContains(package.Id, term))
                {
                    continue;
                }

                if (IsNotNullAndContains(package.Description, term))
                {
                    continue;
                }

                if (IsNotNullAndContains(package.Tags, $" {term} "))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsNotNullAndContains(string input, string value)
        {
            return input != null && 
                   input.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
