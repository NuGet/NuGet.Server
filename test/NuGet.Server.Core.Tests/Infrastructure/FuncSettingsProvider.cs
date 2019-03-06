// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
using System;
using NuGet.Server.Core.Infrastructure;

namespace NuGet.Server.Core.Tests.Infrastructure
{
    class FuncSettingsProvider : ISettingsProvider
    {
        readonly Func<string, object, object> _getSetting;
        internal FuncSettingsProvider(Func<string, object, object> getSetting)
        {
            if (getSetting == null)
            {
                throw new ArgumentNullException(nameof(getSetting));
            }

            _getSetting = getSetting;
        }

        public bool GetBoolSetting(string key, bool defaultValue)
        {
            return Convert.ToBoolean(_getSetting(key, defaultValue));
        }

        public int GetIntSetting(string key, int defaultValue)
        {
            return Convert.ToInt32(_getSetting(key, defaultValue));
        }

        public string GetStringSetting(string key, string defaultValue)
        {
            return Convert.ToString(_getSetting(key, defaultValue));
        }
    }
}
