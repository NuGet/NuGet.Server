// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Server.Core.Infrastructure
{
    public static class ServerPackageRepositoryExtensions
    {
        public static async Task<IEnumerable<IServerPackage>> FindPackagesByIdAsync(
            this IServerPackageRepository repository,
            string id,
            ClientCompatibility compatibility,
            CancellationToken token)
        {
            var packages = await repository.GetPackagesAsync(compatibility, token);

            return packages.Where(p => StringComparer.OrdinalIgnoreCase.Equals(p.Id, id));
        }

        public static async Task<IEnumerable<IServerPackage>> SearchAsync(
            this IServerPackageRepository repository,
            string searchTerm,
            bool allowPrereleaseVersions,
            ClientCompatibility compatibility,
            CancellationToken token)
        {
            return await repository.SearchAsync(
                searchTerm,
                Enumerable.Empty<string>(),
                allowPrereleaseVersions,
                compatibility,
                token);
        }

        public static async Task<IEnumerable<IServerPackage>> SearchAsync(
            this IServerPackageRepository repository,
            string searchTerm,
            bool allowPrereleaseVersions,
            bool allowUnlistedVersions,
            ClientCompatibility compatibility,
            CancellationToken token)
        {
            return await repository.SearchAsync(
                searchTerm,
                Enumerable.Empty<string>(),
                allowPrereleaseVersions,
                allowUnlistedVersions,
                compatibility,
                token);
        }

        public static async Task<IServerPackage> FindPackageAsync(
            this IServerPackageRepository repository,
            string id,
            SemanticVersion version,
            CancellationToken token)
        {
            var packages = await repository.FindPackagesByIdAsync(id, ClientCompatibility.Max, token);

            return packages.FirstOrDefault(p => p.Version.Equals(version));
        }

        public static async Task<bool> ExistsAsync(
            this IServerPackageRepository repository,
            string id,
            SemanticVersion version,
            CancellationToken token)
        {
            var package = await repository.FindPackageAsync(id, version, token);

            return package != null;
        }

        public static async Task<IServerPackage> FindPackageAsync(
            this IServerPackageRepository repository,
            string id,
            ClientCompatibility compatibility,
            CancellationToken token)
        {
            var packages = await repository.FindPackagesByIdAsync(id, compatibility, token);

            return packages
                .OrderByDescending(p => p.Version)
                .FirstOrDefault();
        }

        public static async Task<IEnumerable<IServerPackage>> GetUpdatesAsync(
            this IServerPackageRepository repository,
            IEnumerable<IPackageName> packages,
            bool includePrerelease,
            bool includeAllVersions,
            IEnumerable<FrameworkName> targetFramework,
            IEnumerable<IVersionSpec> versionConstraints,
            ClientCompatibility compatibility,
            CancellationToken token)
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
            var candidates = await GetUpdateCandidatesAsync(
                repository,
                packageList,
                includePrerelease,
                compatibility,
                token);

            ILookup<string, IServerPackage> sourcePackages = candidates
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

        private static async Task<IEnumerable<IServerPackage>> GetUpdateCandidatesAsync(
            IServerPackageRepository repository,
            IEnumerable<IPackageName> packages,
            bool includePrerelease,
            ClientCompatibility compatibility,
            CancellationToken token)
        {
            var query = await repository.GetPackagesAsync(compatibility, token);

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
