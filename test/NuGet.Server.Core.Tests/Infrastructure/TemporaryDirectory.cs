// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;

namespace NuGet.Server.Core.Tests.Infrastructure
{
    public sealed class TemporaryDirectory 
        : IDisposable
    {
        public string Path { get; private set; }

        public TemporaryDirectory()
        {
            var tempPath = System.IO.Path.GetTempPath();
            Path = System.IO.Path.Combine(tempPath, GetType().Assembly.GetName().Name, Guid.NewGuid().ToString());

            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                try
                {
                    Directory.Delete(Path, true);
                }
                catch
                {
                    // Nothing to do here.
                }
            }
        }

        public static implicit operator string(TemporaryDirectory directory)
        {
            return directory.Path;
        }
    }
}