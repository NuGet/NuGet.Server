// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Specialized;
using System.Security.Principal;
using System.Web.Configuration;

namespace NuGet.Server.Infrastructure
{
    public class PackageAuthenticationService : IPackageAuthenticationService
    {
        public bool IsAuthenticated(IPrincipal user, string apiKey, string packageId)
        {
            var appSettings = WebConfigurationManager.AppSettings;
            return IsAuthenticatedInternal(apiKey, appSettings);
        }

        internal static bool IsAuthenticatedInternal(string apiKey, NameValueCollection appSettings)
        {
            bool value;
            if (!Boolean.TryParse(appSettings["requireApiKey"], out value))
            {
                // If the setting is misconfigured, fail.
                return false;
            }

            if (value == false)
            {
                // If the server's configured to allow pushing without an ApiKey, all requests are assumed to be authenticated.
                return true;
            }

            var settingsApiKey = appSettings["apiKey"];

            // No api key, no-one can push
            if (String.IsNullOrEmpty(settingsApiKey))
            {
                return false;
            }

            return string.Equals(apiKey, settingsApiKey, StringComparison.OrdinalIgnoreCase);
        }
    }
}
