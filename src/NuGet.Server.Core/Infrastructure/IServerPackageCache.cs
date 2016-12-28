// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace NuGet.Server.Core.Infrastructure
{
    public interface IServerPackageCache
    {
        bool HasPackages();
        IEnumerable<ServerPackage> GetAll();
        void Add(ServerPackage entity);
        void StoreRange(IEnumerable<ServerPackage> entities);
        void Remove(string id, SemanticVersion version, bool enableDelisting);
        void Persist();
        void PersistIfDirty();
        void Clear();
    }
}