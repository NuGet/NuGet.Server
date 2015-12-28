// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace NuGet.Server.Infrastructure
{
    public class ServerPackage
        : IPackage
    {
        public ServerPackage()
        {
        }

        public ServerPackage(IPackage package, PackageDerivedData packageDerivedData)
        {
            Id = package.Id;
            Version = package.Version;
            Title = package.Title;
            Authors = package.Authors;
            Owners = package.Owners;
            IconUrl = package.IconUrl;
            LicenseUrl = package.LicenseUrl;
            ProjectUrl = package.ProjectUrl;
            RequireLicenseAcceptance = package.RequireLicenseAcceptance;
            DevelopmentDependency = package.DevelopmentDependency;
            Description = package.Description;
            Summary = package.Summary;
            ReleaseNotes = package.ReleaseNotes;
            Language = package.Language;
            Tags = package.Tags;
            Copyright = package.Copyright;
            MinClientVersion = package.MinClientVersion;
            ReportAbuseUrl = package.ReportAbuseUrl;
            DownloadCount = package.DownloadCount;
            IsAbsoluteLatestVersion = package.IsAbsoluteLatestVersion;
            IsLatestVersion = package.IsLatestVersion;
            Listed = package.Listed;
            Published = package.Published;

            Dependencies = DependencySetsAsString(package.DependencySets);
            SupportedFrameworks = string.Join("|", package.GetSupportedFrameworks().Select(VersionUtility.GetFrameworkString));

            PackageSize = packageDerivedData.PackageSize;
            PackageHash = packageDerivedData.PackageHash;
            PackageHashAlgorithm = packageDerivedData.PackageHashAlgorithm;
            LastUpdated = packageDerivedData.LastUpdated;
            Created = packageDerivedData.Created;
            Path = packageDerivedData.Path;
            FullPath = packageDerivedData.FullPath;
        }

        public string Id { get; }

        public SemanticVersion Version { get; }

        public string Title { get; }

        public IEnumerable<string> Authors { get; }

        public IEnumerable<string> Owners { get; }

        public Uri IconUrl { get; }

        public Uri LicenseUrl { get; }

        public Uri ProjectUrl { get; }

        public bool RequireLicenseAcceptance { get; }

        public bool DevelopmentDependency { get; }

        public string Description { get; }

        public string Summary { get; }

        public string ReleaseNotes { get; }

        public string Language { get; }

        public string Tags { get; }

        public string Copyright { get; }

        public string Dependencies { get; }

        public IEnumerable<PackageDependencySet> DependencySets
        {
            get
            {
                if (String.IsNullOrEmpty(Dependencies))
                {
                    return Enumerable.Empty<PackageDependencySet>();
                }

                return ParseDependencySet(Dependencies);
            }
        }

        public Version MinClientVersion { get; }

        public Uri ReportAbuseUrl { get; }

        public int DownloadCount { get; }
        
        public string SupportedFrameworks { get; }

        public IEnumerable<FrameworkName> GetSupportedFrameworks()
        {
            if (!String.IsNullOrEmpty(SupportedFrameworks))
            {
                var supportedFrameworksAsStrings = SupportedFrameworks.Split('|').ToList();
                if (supportedFrameworksAsStrings.Any())
                {
                    return supportedFrameworksAsStrings.Select(VersionUtility.ParseFrameworkName);
                }

            }

            return Enumerable.Empty<FrameworkName>();
        }

        public bool IsAbsoluteLatestVersion { get; internal set; }

        public bool IsLatestVersion { get; internal set; }

        public bool Listed { get; }

        public DateTimeOffset? Published { get; }

        
        public long PackageSize { get; }

        public string PackageHash { get; }

        public string PackageHashAlgorithm { get; }

        public DateTimeOffset LastUpdated { get; }

        public DateTimeOffset Created { get; }

        public string Path { get; }

        public string FullPath { get; }

        private static string DependencySetsAsString(IEnumerable<PackageDependencySet> dependencySets)
        {
            if (dependencySets == null)
            {
                return null;
            }

            var dependencies = new List<string>();
            foreach (var dependencySet in dependencySets)
            {
                if (dependencySet.Dependencies.Count == 0)
                {
                    dependencies.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}", null, null, dependencySet.TargetFramework.ToShortNameOrNull()));
                }
                else
                {
                    foreach (var dependency in dependencySet.Dependencies.Select(d => new { d.Id, d.VersionSpec, dependencySet.TargetFramework }))
                    {
                        dependencies.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}", dependency.Id, dependency.VersionSpec == null ? null : dependency.VersionSpec.ToString(), dependencySet.TargetFramework.ToShortNameOrNull()));
                    }
                }
            }

            return string.Join("|", dependencies);
        }

        private static List<PackageDependencySet> ParseDependencySet(string value)
        {
            var dependencySets = new List<PackageDependencySet>();

            var dependencies = value.Split('|').Select(ParseDependency).ToList();

            // group the dependencies by target framework
            var groups = dependencies.GroupBy(d => d.Item3);

            dependencySets.AddRange(
                groups.Select(g => new PackageDependencySet(
                    g.Key,   // target framework 
                    g.Where(pair => !String.IsNullOrEmpty(pair.Item1))                   // the Id is empty when a group is empty.
                     .Select(pair => new PackageDependency(pair.Item1, pair.Item2)))));  // dependencies by that target framework
            return dependencySets;
        }

        /// <summary>
        /// Parses a dependency from the feed in the format:
        ///     id or id:versionSpec, or id:versionSpec:targetFramework
        /// </summary>
        private static Tuple<string, IVersionSpec, FrameworkName> ParseDependency(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            // IMPORTANT: Do not pass StringSplitOptions.RemoveEmptyEntries to this method, because it will break 
            // if the version spec is null, for in that case, the Dependencies string sent down is "<id>::<target framework>".
            // We do want to preserve the second empty element after the split.
            string[] tokens = value.Trim().Split(new[] { ':' });

            if (tokens.Length == 0)
            {
                return null;
            }

            // Trim the id
            string id = tokens[0].Trim();

            IVersionSpec versionSpec = null;
            if (tokens.Length > 1)
            {
                // Attempt to parse the version
                VersionUtility.TryParseVersionSpec(tokens[1], out versionSpec);
            }

            var targetFramework = (tokens.Length > 2 && !String.IsNullOrEmpty(tokens[2]))
                                    ? VersionUtility.ParseFrameworkName(tokens[2])
                                    : null;

            return Tuple.Create(id, versionSpec, targetFramework);
        }


        #region Unsupported operations

        public IEnumerable<IPackageFile> GetFiles()
        {
            throw new NotImplementedException("The NuGet.Server.ServerPackage type does not support getting files.");
        }

        public Stream GetStream()
        {
            throw new NotImplementedException("The NuGet.Server.ServerPackage type does not support getting a stream.");
        }

        public void ExtractContents(IFileSystem fileSystem, string extractPath)
        {
            throw new NotImplementedException("The NuGet.Server.ServerPackage type does not support extracting contents.");
        }

        public IEnumerable<FrameworkAssemblyReference> FrameworkAssemblies
        {
            get
            {
                throw new NotImplementedException("The NuGet.Server.ServerPackage type does not support enumerating FrameworkAssemblies.");
            }
        }

        public ICollection<PackageReferenceSet> PackageAssemblyReferences
        {
            get
            {
                throw new NotImplementedException("The NuGet.Server.ServerPackage type does not support enumerating PackageAssemblyReferences.");
            }
        }

        public IEnumerable<IPackageAssemblyReference> AssemblyReferences
        {
            get
            {
                throw new NotImplementedException("The NuGet.Server.ServerPackage type does not support enumerating AssemblyReferences.");
            }
        }

        #endregion
    }
}
