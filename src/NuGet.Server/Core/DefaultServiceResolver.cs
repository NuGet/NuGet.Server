// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Specialized;
using System.Web.Configuration;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.Core.Logging;
using NuGet.Server.Infrastructure;

namespace NuGet.Server
{
    public sealed class DefaultServiceResolver
        : IServiceResolver, IDisposable
    {
        private readonly CryptoHashProvider _hashProvider;
        private readonly ServerPackageRepository _packageRepository;
        private readonly PackageAuthenticationService _packageAuthenticationService;
        private readonly WebConfigSettingsProvider _settingsProvider;

        public DefaultServiceResolver() : this(
            PackageUtility.PackagePhysicalPath,
            WebConfigurationManager.AppSettings)
        {
        }

        public DefaultServiceResolver(string packagePath, NameValueCollection settings)
        {
            _hashProvider = new CryptoHashProvider(Core.Constants.HashAlgorithm);

            _settingsProvider = new WebConfigSettingsProvider(settings);

            _packageRepository = new ServerPackageRepository(packagePath, _hashProvider, _settingsProvider, new TraceLogger());

            _packageAuthenticationService = new PackageAuthenticationService(settings);

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

            if (type == typeof(ISettingsProvider))
            {
                return _settingsProvider;
            }

            return null;
        }

        public void Dispose()
        {
            _packageRepository.Dispose();
        }
    }
}