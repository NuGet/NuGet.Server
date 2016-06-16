// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using NuGet.Server.Logging;

namespace NuGet.Server.Tests.Infrastructure
{
    public class NullLogger
        : NuGet.Server.Logging.ILogger
    {
        public void Log(LogLevel level, string message, params object[] args)
        {
        }
    }
}