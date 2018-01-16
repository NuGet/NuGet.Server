// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
using System;
using System.Collections.Generic;
using NuGet.Server.Core.Infrastructure;

namespace NuGet.Server.V2.Samples.OwinHost
{
    public class DictionarySettingsProvider : ISettingsProvider
    {
        readonly Dictionary<string, object> _settings;

        public DictionarySettingsProvider(Dictionary<string, object> settings)
        {
            _settings = settings;
        }


        public bool GetBoolSetting(string key, bool defaultValue)
        {
            System.Diagnostics.Debug.WriteLine("getSetting: " + key);
            return _settings.ContainsKey(key) ? Convert.ToBoolean(_settings[key]) : defaultValue;

        }

        public string GetStringSetting(string key, string defaultValue)
        {
            return _settings.ContainsKey(key) ? Convert.ToString(_settings[key]) : defaultValue;
        }
    }
}
