using NuGet.Server.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Server.V2.OWinSampleHost
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
