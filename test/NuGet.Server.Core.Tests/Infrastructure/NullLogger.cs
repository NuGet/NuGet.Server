// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
using NuGet.Server.Core.Logging;

namespace NuGet.Server.Core.Tests.Infrastructure
{
    public class NullLogger
        : Logging.ILogger
    {
        public void Log(LogLevel level, string message, params object[] args)
        {
        }
    }
}