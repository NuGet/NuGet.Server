// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
namespace NuGet.Server.Core.Infrastructure
{
    public class DefaultSettingsProvider : ISettingsProvider
    {
        public bool GetBoolSetting(string key, bool defaultValue)
        {
            return defaultValue;
        }

        public int GetIntSetting(string key, int defaultValue)
        {
            return defaultValue;
        }

        public string GetStringSetting(string key, string defaultValue)
        {
            return defaultValue;
        }
    }
}
