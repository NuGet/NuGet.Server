// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace NuGet.Server.Core.Infrastructure
{
    public interface IServerPackageStore
    {
        ServerPackage Add(IPackage package, bool enableDelisting);
        bool Exists(string id, SemanticVersion version);
        HashSet<ServerPackage> GetAll(bool enableDelisting);
        void Remove(string id, SemanticVersion version, bool enableDelisting);
    }
}