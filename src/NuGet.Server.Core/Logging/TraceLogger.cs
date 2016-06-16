// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Diagnostics;

namespace NuGet.Server.Logging
{
    public class TraceLogger
        : ILogger
    {
        public void Log(LogLevel level, string message, params object[] args)
        {
            Trace.WriteLine(string.Format(message, args), level.ToString());
        }
    }
}