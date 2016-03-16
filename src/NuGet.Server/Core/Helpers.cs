// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;

namespace NuGet.Server
{
    public static class Helpers
    {
        public static string GetRepositoryUrl(Uri currentUrl, string applicationPath)
        {
            return GetBaseUrl(currentUrl, applicationPath) + "nuget";
        }

        public static string GetPushUrl(Uri currentUrl, string applicationPath)
        {
            return GetBaseUrl(currentUrl, applicationPath) + "nuget";
        }

        public static string GetBaseUrl(Uri currentUrl, string applicationPath)
        {
            var uriBuilder = new UriBuilder(currentUrl);

            var repositoryUrl = uriBuilder.Scheme + "://" + uriBuilder.Host;
            if (uriBuilder.Port != 80 && uriBuilder.Port != 443)
            {
                repositoryUrl += ":" + uriBuilder.Port;
            }

            repositoryUrl += applicationPath;

            // ApplicationPath for Virtual Apps don't end with /
            return EnsureTrailingSlash(repositoryUrl);
        }

        internal static string EnsureTrailingSlash(string path)
        {
            if (String.IsNullOrEmpty(path))
            {
                return path;
            }

            if (!path.EndsWith("/"))
            {
                return path + "/";
            }
            return path;
        }
    }
}
