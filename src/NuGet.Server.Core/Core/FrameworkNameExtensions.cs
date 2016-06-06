// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Runtime.Versioning;

namespace NuGet.Server
{
    internal static class FrameworkNameExtensions
    {
        public static string ToShortNameOrNull(this FrameworkName current)
        {
            if (current == null)
            {
                return string.Empty;
            }

            return VersionUtility.GetShortFrameworkName(current);
        }
    }
}