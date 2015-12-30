// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

namespace NuGet.Server.Infrastructure
{
    public interface IServerPackageRepository 
        : IServiceBasedRepository
    {
        void ClearCache();
        void RemovePackage(string packageId, SemanticVersion version);
    }
}
