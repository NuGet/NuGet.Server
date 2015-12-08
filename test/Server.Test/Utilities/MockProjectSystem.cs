using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;

namespace NuGet.Test.Mocks
{
    public class MockProjectSystem : MockFileSystem, IProjectSystem
    {
        private FrameworkName _frameworkName;
        private HashSet<string> _topImports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _bottomImports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _excludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _projectName;

        public MockProjectSystem()
            : this(VersionUtility.DefaultTargetFramework)
        {
        }

        public MockProjectSystem(FrameworkName frameworkName, string root = @"x:\MockFileSystem")
            : base(root)
        {
            _frameworkName = frameworkName;
            References = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public bool IsBindingRedirectSupported
        {
            get
            {
                return true;
            }
        }

        public virtual Dictionary<string, string> References
        {
            get;
            private set;
        }

        public void AddReference(string referencePath)
        {
            AddReference(referencePath, null);
        }

        public virtual void AddReference(string referencePath, Stream stream)
        {
            References.Add(Path.GetFileName(referencePath), referencePath);
        }

        public virtual void RemoveReference(string name)
        {
            References.Remove(name);
            DeleteFile(name);
        }

        public virtual bool ReferenceExists(string name)
        {
            return References.ContainsKey(name);
        }

        public virtual FrameworkName TargetFramework
        {
            get { return _frameworkName; }
        }

        public virtual dynamic GetPropertyValue(string propertyName)
        {
            return null;
        }

        public virtual string ProjectName
        {
            get 
            { 
                return _projectName ?? Root; 
            }
            set
            {
                _projectName = value;
            }
        }

        public virtual bool IsSupportedFile(string path)
        {
            return true;
        }

        public void AddFrameworkReference(string name)
        {
            References[name] = name;
        }

        public virtual string ResolvePath(string path)
        {
            return path;
        }

        public void ChangeTargetFramework(FrameworkName newTargetFramework)
        {
            _frameworkName = newTargetFramework;
        }

        public void AddImport(string targetPath, ProjectImportLocation location)
        {
            if (location == ProjectImportLocation.Top)
            {
                _topImports.Add(targetPath);
            }
            else
            {
                _bottomImports.Add(targetPath);
            }
        }

        public void RemoveImport(string targetPath)
        {
            _topImports.Remove(targetPath);
            _bottomImports.Remove(targetPath);
        }

        public bool ImportExists(string targetPath)
        {
            return _topImports.Contains(targetPath) || _bottomImports.Contains(targetPath);
        }

        public bool ImportExists(string targetPath, ProjectImportLocation location)
        {
            if (location == ProjectImportLocation.Top)
            {
                return _topImports.Contains(targetPath);
            }
            else
            {
                return _bottomImports.Contains(targetPath);
            }
        }

        public void ExcludeFileFromProject(string path)
        {
            _excludedFiles.Add(path);
        }

        public bool FileExistsInProject(string path)
        {
            return FileExists(path) && !_excludedFiles.Contains(path);
        }
    }
}