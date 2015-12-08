// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// ReSharper disable DoNotCallOverridableMethodsInConstructor

namespace NuGet.Server.Tests.Utilities.Mocks
{
    public class MockFileSystem : IFileSystem
    {
        private ILogger _logger;
        private readonly Dictionary<string, DateTime> _createdTime;

        public MockFileSystem()
            : this(@"C:\MockFileSystem\")
        {

        }

        public MockFileSystem(string root)
        {
            Root = root.TrimEnd(Path.DirectorySeparatorChar);
            if (Root.EndsWith(":"))
            {
                Root += Path.DirectorySeparatorChar;
            }

            Paths = new Dictionary<string, Func<Stream>>(StringComparer.OrdinalIgnoreCase);
            Deleted = new HashSet<string>();
            _createdTime = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        }

        public virtual ILogger Logger
        {
            get
            {
                return _logger ?? NullLogger.Instance;
            }
            set
            {
                _logger = value;
            }
        }

        

        public virtual string Root
        {
            get;
            private set;
        }

        public virtual IDictionary<string, Func<Stream>> Paths
        {
            get;
            private set;
        }

        public virtual HashSet<string> Deleted
        {
            get;
            private set;
        }

        public virtual void CreateDirectory(string path)
        {
            path = NormalizePath(path);
            Paths.Add(path, null);
        }

        public virtual void DeleteDirectory(string path, bool recursive = false)
        {
            path = NormalizePath(path);
            foreach (var file in Paths.Keys.ToList())
            {
                if (file.StartsWith(path))
                {
                    Paths.Remove(file);
                }
            }
            Deleted.Add(path);
        }

        public virtual string GetFullPath(string path)
        {
            return Path.Combine(Root, path);
        }

        public virtual IEnumerable<string> GetFiles(string path, bool recursive)
        {
            path = PathUtility.EnsureTrailingSlash(NormalizePath(path));
            var files = Paths.Select(f => f.Key);
            if (recursive)
            {
                files = files.Where(f => f.StartsWith(path, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                files = files.Where(f =>
                {
                    var d = PathUtility.EnsureTrailingSlash(Path.GetDirectoryName(f));
                    return StringComparer.OrdinalIgnoreCase.Equals(d, path);
                });
            }

            var retValue = files.Select(MakeRelativePath).ToList();
            return retValue;
        }

        protected string MakeRelativePath(string fullPath)
        {
            return fullPath.Substring(Root.Length).TrimStart(Path.DirectorySeparatorChar);
        }

        public virtual IEnumerable<string> GetFiles(string path, string filter, bool recursive)
        {
            if (String.IsNullOrEmpty(filter) || filter == "*.*")
            {
                filter = "*";
            }

            var files = GetFiles(path, recursive);
            if (!filter.Contains("*"))
            {
                return files.Where(f => f.Equals(Path.Combine(path, filter), StringComparison.OrdinalIgnoreCase));
            }

            var matcher = GetFilterRegex(filter);
            var retValue = files.Where(f => matcher.IsMatch(f)).ToList();
            return retValue;
        }

        private static Regex GetFilterRegex(string wildcard)
        {
            var pattern = '^' + String.Join(@"\.", wildcard.Split('.').Select(GetPattern)) + '$';
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);
        }

        private static string GetPattern(string token)
        {
            return token.Replace("*", "(.*)");
        }

        private string NormalizePath(string path)
        {
            return Path.GetFullPath(Path.Combine(Root, path));
        }

        public virtual void DeleteFile(string path)
        {
            path = NormalizePath(path);
            Paths.Remove(path);
            Deleted.Add(path);
        }

        public virtual void DeleteFiles(IEnumerable<IPackageFile> files, string rootDir)
        {
            FileSystemExtensions2.DeleteFiles(this, files, rootDir);
        }

        public virtual bool FileExists(string path)
        {
            path = NormalizePath(path);
            return Paths.ContainsKey(path);
        }

        public virtual Stream OpenFile(string path)
        {
            path = NormalizePath(path);

            Func<Stream> factory;
            if (!Paths.TryGetValue(path, out factory))
            {
                throw new FileNotFoundException(path + " not found.");
            }
            return factory();
        }

        public virtual Stream CreateFile(string path)
        {
            path = NormalizePath(path);
            Paths[path] = () => Stream.Null;
            
            Action<Stream> streamClose = stream => {
                stream.Seek(0, SeekOrigin.Begin);
                AddFile(path, stream);
            };
            var memoryStream = new EventMemoryStream(streamClose);
            return memoryStream;
        }

        public string ReadAllText(string path)
        {
            return OpenFile(path).ReadToEnd();
        }

        public virtual bool DirectoryExists(string path)
        {
            path = NormalizePath(path);
            var pathPrefix = PathUtility.EnsureTrailingSlash(path);
            return Paths.Keys
                        .Any(file => file.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                                     file.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase));
        }

        public virtual IEnumerable<string> GetDirectories(string path)
        {
            path = NormalizePath(path).TrimEnd(Path.DirectorySeparatorChar);
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            directories.AddRange(Paths.Select(f => Path.GetDirectoryName(f.Key)));
            var subDirectories = directories
                .Where(d =>
                {   
                    if (d.StartsWith(path, StringComparison.OrdinalIgnoreCase)
                        && !StringComparer.OrdinalIgnoreCase.Equals(d, path))
                    {
                        // d is a subdirectory of path. Now checks if it is one level deep.
                        var s = d.Substring(path.Length).TrimStart(Path.DirectorySeparatorChar);
                        return !s.Contains(Path.DirectorySeparatorChar);
                    }

                    return false;
                })
                .Select(MakeRelativePath);
            return subDirectories;
        }

        public virtual void AddFile(string path)
        {
            AddFile(path, Stream.Null);
        }

        public void AddFile(string path, string content)
        {
            AddFile(path, content.AsStream());
        }

        public virtual void AddFile(string path, Stream stream, bool overrideIfExists)
        {
            path = NormalizePath(path);
            var ms = new MemoryStream((int)stream.Length);
            stream.CopyTo(ms);
            var buffer = ms.ToArray();
            Paths[path] = () => new MemoryStream(buffer);
            _createdTime[path] = DateTime.UtcNow;
        }

        public virtual void AddFile(string path, Stream stream)
        {
            AddFile(path, stream, overrideIfExists: true);
        }

        public virtual void AddFile(string path, Action<Stream> writeToStream)
        {
            path = NormalizePath(path);
            var ms = new MemoryStream();
            writeToStream(ms);
            var buffer = ms.ToArray();
            Paths[path] = () => new MemoryStream(buffer);
            _createdTime[path] = DateTime.UtcNow;
        }

        public virtual void AddFiles(IEnumerable<IPackageFile> files, string rootDir)
        {
            FileSystemExtensions2.AddFiles(this, files, rootDir);
        }

        public virtual void AddFile(string path, Func<Stream> getStream)
        {
            path = NormalizePath(path);
            Paths[path] = getStream;
        }

        public virtual DateTimeOffset GetLastModified(string path)
        {
            path = NormalizePath(path);
            DateTime time;
            if (_createdTime.TryGetValue(path, out time))
            {
                return time;
            }
            else
            {
                return DateTime.UtcNow;
            }
        }

        public virtual DateTimeOffset GetCreated(string path)
        {
            path = NormalizePath(path);
            DateTime time;
            if (_createdTime.TryGetValue(path, out time))
            {
                return time;
            }
            else
            {
                return DateTime.UtcNow;
            }
        }

        public virtual DateTimeOffset GetLastAccessed(string path)
        {
            path = NormalizePath(path);
            DateTime time;
            if (_createdTime.TryGetValue(path, out time))
            {
                return time;
            }
            else
            {
                return DateTime.UtcNow;
            }
        }


        public void MakeFileWritable(string path)
        {
            // Nothing to do here.
        }

        public virtual void MoveFile(string src, string destination)
        {
            src = NormalizePath(src);
            destination = NormalizePath(destination);

            Paths.Add(destination, Paths[src]);
            Paths.Remove(src);
        }
    }
}