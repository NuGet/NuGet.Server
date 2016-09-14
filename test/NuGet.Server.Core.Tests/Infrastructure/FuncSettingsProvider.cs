// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
using System;
using NuGet.Server.Core.Infrastructure;

namespace NuGet.Server.Core.Tests.Infrastructure
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
