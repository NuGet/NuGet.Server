// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Web.Configuration;
using NuGet.Server.Core.Infrastructure;

namespace NuGet.Server.Infrastructure
{
    public class WebConfigSettingsProvider : ISettingsProvider
    {
        public bool GetBoolSetting(string key, bool defaultValue)
        {
            var appSettings = WebConfigurationManager.AppSettings;
            bool value;
            return !Boolean.TryParse(appSettings[key], out value) ? defaultValue : value;
        }
    }
}