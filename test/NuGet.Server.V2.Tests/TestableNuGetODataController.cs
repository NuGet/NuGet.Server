// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.Controllers;

namespace NuGet.Server.V2.Tests
{
    public class TestableNuGetODataController : NuGetODataController
    {
        public TestableNuGetODataController(IServerPackageRepository serverPackageRepository)
            :base(serverPackageRepository, null)
        {
        }
    }
}
