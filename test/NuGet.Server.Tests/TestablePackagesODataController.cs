﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using NuGet.Server.DataServices;

namespace NuGet.Server.Tests
{
    public class TestablePackagesODataController : PackagesODataController
    {
        public TestablePackagesODataController(IServiceResolver serviceResolver)
            : base(serviceResolver)
        {
        }
    }
}
