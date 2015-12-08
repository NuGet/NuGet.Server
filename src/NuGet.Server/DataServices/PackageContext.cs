// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Linq;
using NuGet.Server.Infrastructure;

namespace NuGet.Server.DataServices
{
    public class PackageContext
    {
        private readonly IServerPackageRepository _repository;
        public PackageContext(IServerPackageRepository repository)
        {
            _repository = repository;
        }

        public IQueryable<Package> Packages
        {
            get
            {
                return _repository.GetPackages()
                            .Select(_repository.GetMetadataPackage)
                            .Where(p => p != null)
                            .AsQueryable()
                            .InterceptWith(new PackageIdComparisonVisitor());
            }
        }
    }
}
