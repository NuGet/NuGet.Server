// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Diagnostics;
using System.IO;

namespace NuGet.Server.Tests.Utilities
{
    internal class EventMemoryStream : MemoryStream
    {
        private static Action<Stream> _closeAction;

        public EventMemoryStream(Action<Stream> closeAction)
        {
            Debug.Assert(closeAction != null);
            _closeAction = closeAction;
        }

        public override void Close()
        {
            _closeAction(this);
            base.Close();
        }
    }
}