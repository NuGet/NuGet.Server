// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Collections.Generic;
using System.Linq;

namespace NuGet.Server.Infrastructure
{
    public interface IServerPackageStore
    {
        bool HasPackages();
        IQueryable<ServerPackage> GetAll();
        void Store(ServerPackage entity);
        void StoreRange(IEnumerable<ServerPackage> entities);
        void Remove(ServerPackage entity);
        void Remove(string id, SemanticVersion version);
        void Persist();
        void PersistIfDirty();
        void Clear();
    }
}