// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using NuGet.Server.Core.Logging;
using Xunit.Abstractions;

namespace NuGet.Server.Core.Tests
{
    public class TestOutputLogger : Logging.ILogger
    {
        private readonly ITestOutputHelper _output;
        private ConcurrentQueue<string> _messages;

        public TestOutputLogger(ITestOutputHelper output)
        {
            _output = output;
            _messages = new ConcurrentQueue<string>();
        }

        public IEnumerable<string> Messages => _messages;

        public void Clear()
        {
            _messages = new ConcurrentQueue<string>();
        }

        public void Log(LogLevel level, string message, params object[] args)
        {
            var formattedMessage = $"[{level.ToString().Substring(0, 4).ToUpperInvariant()}] {string.Format(message, args)}";
            _messages.Enqueue(formattedMessage);
            _output.WriteLine(formattedMessage);
        }
    }
}
