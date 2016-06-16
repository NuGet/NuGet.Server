using NuGet.Server.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Server.Tests.Infrastructure
{
    class FuncSettingsProvider : ISettingsProvider
    {
        readonly Func<string, bool, bool> _getSetting;
        internal FuncSettingsProvider(Func<string,bool,bool> getSetting)
        {
            if (getSetting == null)
            {
                throw new ArgumentNullException(nameof(getSetting));
            }

            _getSetting = getSetting;
        }

        public bool GetBoolSetting(string key, bool defaultValue)
        {
            return _getSetting(key, defaultValue);
        }
    }
}
