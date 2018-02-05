// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.IO;

namespace NuGet.Server.Core
{
    /// <summary>
    /// A file system implementation that persists nothing. This is intended to be used with
    /// <see cref="OptimizedZipPackage"/> so that package files are never actually extracted anywhere on disk.
    /// </summary>
    public class NullFileSystem : IFileSystem
    {
        public static NullFileSystem Instance { get; } = new NullFileSystem();

        public Stream CreateFile(string path) => Stream.Null;
        public bool DirectoryExists(string path) => true;
        public bool FileExists(string path) => false;
        public string GetFullPath(string path) => null;
        public Stream OpenFile(string path) => Stream.Null;

        public ILogger Logger { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public string Root => throw new NotSupportedException();
        public void AddFile(string path, Stream stream) => throw new NotSupportedException();
        public void AddFile(string path, Action<Stream> writeToStream) => throw new NotSupportedException();
        public void AddFiles(IEnumerable<IPackageFile> files, string rootDir) => throw new NotSupportedException();
        public void DeleteDirectory(string path, bool recursive) => throw new NotSupportedException();
        public void DeleteFile(string path) => throw new NotSupportedException();
        public void DeleteFiles(IEnumerable<IPackageFile> files, string rootDir) => throw new NotSupportedException();
        public DateTimeOffset GetCreated(string path) => throw new NotSupportedException();
        public IEnumerable<string> GetDirectories(string path) => throw new NotSupportedException();
        public IEnumerable<string> GetFiles(string path, string filter, bool recursive) => throw new NotSupportedException();
        public DateTimeOffset GetLastAccessed(string path) => throw new NotSupportedException();
        public DateTimeOffset GetLastModified(string path) => throw new NotSupportedException();
        public void MakeFileWritable(string path) => throw new NotSupportedException();
        public void MoveFile(string source, string destination) => throw new NotSupportedException();
    }
}
