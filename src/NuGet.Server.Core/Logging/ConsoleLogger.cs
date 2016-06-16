// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;

namespace NuGet.Server.Logging
{
    public class ConsoleLogger
        : ILogger
    {
        public void Log(LogLevel level, string message, params object[] args)
        {
            switch (level)
            {
                case LogLevel.Verbose:
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    break;
                case LogLevel.Info:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;
                case LogLevel.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case LogLevel.Error:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
            }

            Console.WriteLine(message, args);
        }
    }
}