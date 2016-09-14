// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
using System.Security.Principal;

namespace NuGet.Server.Core.Infrastructure
{
    public interface IPackageAuthenticationService
    {
        bool IsAuthenticated(IPrincipal user, string apiKey, string packageId);
    }
}