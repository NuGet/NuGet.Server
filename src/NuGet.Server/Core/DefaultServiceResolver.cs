// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
using System;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.Core.Logging;
using NuGet.Server.Infrastructure;

namespace NuGet.Server
{
    public class DefaultServiceResolver
        : IServiceResolver
    {
        private readonly IHashProvider _hashProvider;
        private readonly IServerPackageRepository _packageRepository;
        private readonly IPackageAuthenticationService _packageAuthenticationService;
        private readonly ISettingsProvider _settingsProvider;

        public DefaultServiceResolver()
        {
            _hashProvider = new CryptoHashProvider(Core.Constants.HashAlgorithm);

            _settingsProvider = new WebConfigSettingsProvider();

            _packageRepository = new ServerPackageRepository(PackageUtility.PackagePhysicalPath, _hashProvider, _settingsProvider, new TraceLogger());

            _packageAuthenticationService = new PackageAuthenticationService();

        }

        public object Resolve(Type type)
        {
            if (type == typeof(IHashProvider))
            {
                return _hashProvider;
            }

            if (type == typeof(IServerPackageRepository))
            {
                return _packageRepository;
            }

            if (type == typeof(IPackageAuthenticationService))
            {
                return _packageAuthenticationService;
            }


            return null;
        }
    }
}