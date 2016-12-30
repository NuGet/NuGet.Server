// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Text.RegularExpressions;
using NuGet.Server.Infrastructure;

namespace NuGet.Server.DataServices
{
    public static class ClientCompatibilityFactory
    {
        private const string SemVerLevelKey = "semVerLevel";

        private static readonly Regex ExtractPackageVersion = new Regex(
            @"/Packages\(\s*Id\s*=.+?,\s*Version\s*=\s*.+\)$",
            RegexOptions.IgnoreCase);

        public static ClientCompatibility FromUri(Uri uri)
        {
            if (uri == null)
            {
                return ClientCompatibility.Default;
            }
            
            // If the URI is pointing to a specific ID or version, then disable filtering on SemVer 2.0.0.
            if (IsSpecificPackageUri(uri))
            {
                return ClientCompatibility.Max; 
            }

            // Observe the SemVer level, if provided.
            string unparsedSemVerLevel = GetSemVerLevelParameter(uri);

            return FromProperties(unparsedSemVerLevel);
        }

        public static ClientCompatibility FromProperties(string unparsedSemVerLevel)
        {
            SemanticVersion semVerLevel;
            if (string.IsNullOrWhiteSpace(unparsedSemVerLevel) ||
                !SemanticVersion.TryParse(unparsedSemVerLevel, out semVerLevel))
            {
                semVerLevel = ClientCompatibility.Default.SemVerLevel;
            }

            if (semVerLevel == ClientCompatibility.Default.SemVerLevel)
            {
                return ClientCompatibility.Default;
            }
            else if (semVerLevel == ClientCompatibility.Max.SemVerLevel)
            {
                return ClientCompatibility.Max;
            }

            return new ClientCompatibility(semVerLevel);
        }

        private static bool IsSpecificPackageUri(Uri uri)
        {
            return ExtractPackageVersion.IsMatch(uri.LocalPath ?? string.Empty);
        }

        private static string GetSemVerLevelParameter(Uri uri)
        {
            // Set the SemVer level based on the query string.
            var unparsedQuery = uri.Query ?? string.Empty;
            var query = System.Web.HttpUtility.ParseQueryString(unparsedQuery);
            return query[SemVerLevelKey];
        }
    }
}