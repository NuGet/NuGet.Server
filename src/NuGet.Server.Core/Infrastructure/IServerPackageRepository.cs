// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;

namespace NuGet.Server.Core.Infrastructure
{
    public interface IServerPackageRepository
    {
        string Source { get; }

        void AddPackage(IPackage package);

        IQueryable<IServerPackage> GetPackages();

        IQueryable<IServerPackage> Search(
            string searchTerm,
            IEnumerable<string> targetFrameworks,
            bool allowPrereleaseVersions);

        void ClearCache();

        void RemovePackage(string packageId, SemanticVersion version);
    }
}
