using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Server.Infrastructure
{
    public interface ISettingsProvider
    {
        bool GetBoolSetting(string key, bool defaultValue);
    }
}
