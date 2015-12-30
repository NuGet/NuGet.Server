// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;

namespace NuGet.Server.Infrastructure
{
    public class PackageDerivedData
    {
        public PackageDerivedData()
        {
            PackageHashAlgorithm = Constants.HashAlgorithm;
        }

        public long PackageSize { get; set; }
        public string PackageHash { get; set; }
        public string PackageHashAlgorithm { get; set; }
        public DateTimeOffset LastUpdated { get; set; }
        public DateTimeOffset Created { get; set; }
        public string Path { get; set; }
        public string FullPath { get; set; }
    }
}