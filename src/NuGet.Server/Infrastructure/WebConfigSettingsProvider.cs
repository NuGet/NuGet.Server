// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Specialized;
using System.Web.Configuration;
using NuGet.Server.Core.Infrastructure;

namespace NuGet.Server.Infrastructure
{
    public class WebConfigSettingsProvider : ISettingsProvider
    {
        private readonly Func<NameValueCollection> _getSettings;

        public WebConfigSettingsProvider()
        {
            _getSettings = () => WebConfigurationManager.AppSettings;
        }

        public WebConfigSettingsProvider(NameValueCollection settings)
        {
            _getSettings = () => settings;
        }

        public bool GetBoolSetting(string key, bool defaultValue)
        {
            var settings = _getSettings();
            bool value;
            return !bool.TryParse(settings[key], out value) ? defaultValue : value;
        }

        public int GetIntSetting(string key, int defaultValue)
        {
            var settings = _getSettings();
            int value;
            return !int.TryParse(settings[key], out value) ? defaultValue : value;
        }

        public string GetStringSetting(string key, string defaultValue)
        {
            var settings = _getSettings();
            return settings[key] ?? defaultValue;
        }
    }
}