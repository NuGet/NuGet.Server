// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;

namespace NuGet.Server.Core.Infrastructure
{
    public static class ServerPackageRepositoryExtensions
    {
        public static IQueryable<IServerPackage> FindPackagesById(
            this IServerPackageRepository repository,
            string id)
        {
            return repository
                .GetPackages()
                .Where(p => StringComparer.OrdinalIgnoreCase.Equals(p.Id, id));
        }

        public static IQueryable<IServerPackage> Search(
            this IServerPackageRepository repository,
            string searchTerm,
            bool allowPrereleaseVersions)
        {
            return repository.Search(searchTerm, Enumerable.Empty<string>(), allowPrereleaseVersions);
        }

        public static IServerPackage FindPackage(
            this IServerPackageRepository repository,
            string id,
            SemanticVersion version)
        {
            return repository
                .FindPackagesById(id)
                .FirstOrDefault(p => p.Version.Equals(version));
        }

        public static IServerPackage FindPackage(this IServerPackageRepository repository, string id)
        {
            return repository
                .FindPackagesById(id)
                .OrderByDescending(p => p.Version)
                .FirstOrDefault();
        }

        public static IEnumerable<IServerPackage> GetUpdates(
            this IServerPackageRepository repository,
            IEnumerable<IPackageName> packages,
            bool includePrerelease,
            bool includeAllVersions,
            IEnumerable<FrameworkName> targetFramework,
            IEnumerable<IVersionSpec> versionConstraints)
        {
            List<IPackageName> packageList = packages.ToList();

            if (!packageList.Any())
            {
                return Enumerable.Empty<IServerPackage>();
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
            ILookup<string, IServerPackage> sourcePackages = GetUpdateCandidates(repository, packageList, includePrerelease)
                .ToList()
                .ToLookup(package => package.Id, StringComparer.OrdinalIgnoreCase);

            var results = new List<IServerPackage>();
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

        private static bool SupportsTargetFrameworks(IEnumerable<FrameworkName> targetFramework, IServerPackage package)
        {
            return targetFramework.IsEmpty() ||
                   targetFramework.Any(t => VersionUtility.IsCompatible(t, package.GetSupportedFrameworks()));
        }

        /// <summary>
        /// Collapses the packages by Id picking up the highest version for each Id that it encounters
        /// </summary>
        private static IEnumerable<IServerPackage> CollapseById(IEnumerable<IServerPackage> source)
        {
            return source
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(p => p.Version).First());
        }

        private static IQueryable<IServerPackage> GetUpdateCandidates(
            IServerPackageRepository repository,
            IEnumerable<IPackageName> packages,
            bool includePrerelease)
        {
            var query = repository.GetPackages();

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
