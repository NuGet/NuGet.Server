// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
using System.Collections.Generic;
using NuGet.Server.Core.Infrastructure;

namespace NuGet.Server.V2.Samples.OwinHost
{
    public class DictionarySettingsProvider : ISettingsProvider
    {
        readonly Dictionary<string, bool> _settings;

        public DictionarySettingsProvider(Dictionary<string, bool> settings)
        {
            _settings = settings;
        }


        public bool GetBoolSetting(string key, bool defaultValue)
        {
            System.Diagnostics.Debug.WriteLine("getSetting: " + key);
            return _settings.ContainsKey(key) ? _settings[key] : defaultValue;

        }
    }
}
