// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

namespace NuGet.Server.Core.Infrastructure
{
    public static class ClientCompatibilityFactory
    {
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
    }
}