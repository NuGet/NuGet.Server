// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Specialized;
using System.Security.Principal;
using System.Web.Configuration;
using NuGet.Server.Core.Infrastructure;

namespace NuGet.Server.Infrastructure
{
    public class PackageAuthenticationService : IPackageAuthenticationService
    {
        private readonly Func<NameValueCollection> _getSettings;

        public PackageAuthenticationService()
        {
            _getSettings = () => WebConfigurationManager.AppSettings;
        }

        public PackageAuthenticationService(NameValueCollection settings)
        {
            _getSettings = () => settings;
        }

        public bool IsAuthenticated(IPrincipal user, string apiKey, string packageId)
        {
            var settings = _getSettings();

            bool value;
            if (!bool.TryParse(settings["requireApiKey"], out value))
            {
                // If the setting is misconfigured, fail.
                return false;
            }

            if (value == false)
            {
                // If the server's configured to allow pushing without an ApiKey, all requests are assumed to be authenticated.
                return true;
            }

            var settingsApiKey = settings["apiKey"];

            // No api key, no-one can push
            if (string.IsNullOrEmpty(settingsApiKey))
            {
                return false;
            }

            return string.Equals(apiKey, settingsApiKey, StringComparison.OrdinalIgnoreCase);
        }
    }
}
