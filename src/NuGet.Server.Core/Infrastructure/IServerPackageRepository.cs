// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;

namespace NuGet.Server.Core.Infrastructure
{
    public interface IServerPackageRepository
    {
        string Source { get; }

        void AddPackage(IPackage package);

        IQueryable<IServerPackage> GetPackages();

        IEnumerable<IServerPackage> GetUpdates(
            IEnumerable<IPackageName> packages,
            bool includePrerelease,
            bool includeAllVersions,
            IEnumerable<FrameworkName> targetFrameworks,
            IEnumerable<IVersionSpec> versionConstraints);

        IQueryable<IServerPackage> Search(
            string searchTerm,
            IEnumerable<string> targetFrameworks,
            bool allowPrereleaseVersions);

        void ClearCache();

        void RemovePackage(string packageId, SemanticVersion version);
    }
}
