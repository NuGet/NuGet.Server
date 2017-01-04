// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace NuGet.Server.Core.Infrastructure
{
    /// <summary>
    /// This interface provides a relatively high performance view of the packages available on the server. The default
    /// implementation of this interface stores package metadata in memory and persists it to disk as a JSON file, which
    /// is much faster to read than opening up all of the .nupkg each time their metadata is needed.
    /// </summary>
    public interface IServerPackageCache
    {
        bool IsEmpty();

        IEnumerable<ServerPackage> GetAll();

        void Add(ServerPackage entity);

        void AddRange(IEnumerable<ServerPackage> entities);

        void Remove(string id, SemanticVersion version, bool enableDelisting);

        void Persist();

        void PersistIfDirty();

        void Clear();
    }
}