// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;

namespace NuGet.Server.Infrastructure
{
    public static class ServerPackageRepositoryExtensions
    {
        public static IEnumerable<IPackage> GetUpdatesCore(
            this IServerPackageRepository repository,
            IEnumerable<IPackageName> packages,
            bool includePrerelease,
            bool includeAllVersions,
            IEnumerable<FrameworkName> targetFramework,
            IEnumerable<IVersionSpec> versionConstraints,
            ClientCompatibility compatibility)
        {
            List<IPackageName> packageList = packages.ToList();

            if (!packageList.Any())
            {
                return Enumerable.Empty<IPackage>();
            }

            IList<IVersionSpec> versionConstraintList;
            if (versionConstraints == null)
            {
                versionConstraintList = new IVersionSpec[packageList.Count];
            }
            else
            {
                versionConstraintList = versionConstraints.ToList();
            }

            if (packageList.Count != versionConstraintList.Count)
            {
                throw new ArgumentException(Strings.GetUpdatesParameterMismatch);
            }

            // These are the packages that we need to look at for potential updates.
            ILookup<string, IPackage> sourcePackages = GetUpdateCandidates(
                repository,
                packageList,
                includePrerelease,
                compatibility)
                    .ToList()
                    .ToLookup(package => package.Id, StringComparer.OrdinalIgnoreCase);

            var results = new List<IPackage>();
            for (int i = 0; i < packageList.Count; i++)
            {
                var package = packageList[i];
                var constraint = versionConstraintList[i];

                var updates = from candidate in sourcePackages[package.Id]
                              where (candidate.Version > package.Version) &&
                                     SupportsTargetFrameworks(targetFramework, candidate) &&
                                     (constraint == null || constraint.Satisfies(candidate.Version))
                              select candidate;

                results.AddRange(updates);
            }

            if (!includeAllVersions)
            {
                return CollapseById(results);
            }
            return results;
        }

        private static bool SupportsTargetFrameworks(IEnumerable<FrameworkName> targetFramework, IPackage package)
        {
            return targetFramework.IsEmpty() ||
                   targetFramework.Any(t => VersionUtility.IsCompatible(t, package.GetSupportedFrameworks()));
        }

        /// <summary>
        /// Collapses the packages by Id picking up the highest version for each Id that it encounters
        /// </summary>
        private static IEnumerable<IPackage> CollapseById(IEnumerable<IPackage> source)
        {
            return source
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(p => p.Version).First());
        }

        private static IQueryable<IPackage> GetUpdateCandidates(
            IServerPackageRepository repository,
            IEnumerable<IPackageName> packages,
            bool includePrerelease,
            ClientCompatibility compatibility)
        {
            var query = repository.GetPackages(compatibility);

            var ids = new HashSet<string>(
                packages.Select(p => p.Id),
                StringComparer.OrdinalIgnoreCase);

            query = query.Where(p => ids.Contains(p.Id));

            if (!includePrerelease)
            {
                query = query.Where(p => p.IsReleaseVersion());
            }

            // for updates, we never consider unlisted packages
            query = query.Where(p => p.Listed);

            return query;
        }
    }
}