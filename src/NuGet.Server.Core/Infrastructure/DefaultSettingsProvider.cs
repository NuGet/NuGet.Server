using NuGet.Server.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Server.Core.Infrastructure
{
    public class DefaultSettingsProvider : ISettingsProvider
    {
        public bool GetBoolSetting(string key, bool defaultValue)
        {
            return defaultValue;
        }
    }
}
