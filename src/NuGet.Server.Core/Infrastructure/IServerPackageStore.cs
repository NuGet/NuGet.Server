// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Server.Core.Infrastructure
{
    /// <summary>
    /// This interface provides a way to add and remove packages from disk. This store is meant to be the definitive
    /// collection of packages available. If the metadata cache (see <see cref="IServerPackageCache"/>) is out of date
    /// or corrupted, all metadata should be able to be rebuilt by enumerating a reading packages in this store.
    /// </summary>
    public interface IServerPackageStore
    {
        ServerPackage Add(IPackage package, bool enableDelisting);

        bool Exists(string id, SemanticVersion version);

        Task<HashSet<ServerPackage>> GetAllAsync(bool enableDelisting, CancellationToken token);

        void Remove(string id, SemanticVersion version, bool enableDelisting);
    }
}