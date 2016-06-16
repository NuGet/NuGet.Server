using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
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