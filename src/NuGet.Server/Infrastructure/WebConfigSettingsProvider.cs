// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Specialized;
using System.Web.Configuration;
using NuGet.Server.Core.Infrastructure;

namespace NuGet.Server.Infrastructure
{
    public class WebConfigSettingsProvider : ISettingsProvider
    {
        private readonly NameValueCollection _settings;

        public WebConfigSettingsProvider() : this(WebConfigurationManager.AppSettings)
        {
        }

        public WebConfigSettingsProvider(NameValueCollection settings)
        {
            _settings = settings;
        }

        public bool GetBoolSetting(string key, bool defaultValue)
        {
            bool value;
            return !bool.TryParse(_settings[key], out value) ? defaultValue : value;
        }
    }
}