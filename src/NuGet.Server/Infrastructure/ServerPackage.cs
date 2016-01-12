// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Newtonsoft.Json;

namespace NuGet.Server.Infrastructure
{
    public class ServerPackage
        : IPackage
    {
        public ServerPackage()
        {
        }

        public ServerPackage(string id, SemanticVersion version, string title, IEnumerable<string> authors, IEnumerable<string> owners, Uri iconUrl, Uri licenseUrl, Uri projectUrl, bool requireLicenseAcceptance, bool developmentDependency, string description, string summary, string releaseNotes, string language, string tags, string copyright, string dependencies, Version minClientVersion, Uri reportAbuseUrl, int downloadCount, string supportedFrameworks, bool isAbsoluteLatestVersion, bool isLatestVersion, bool listed, DateTimeOffset? published, long packageSize, string packageHash, string packageHashAlgorithm, DateTimeOffset lastUpdated, DateTimeOffset created, string path, string fullPath)
        {
            Id = id;
            Version = version;
            Title = title;
            Authors = authors;
            Owners = owners;
            IconUrl = iconUrl;
            LicenseUrl = licenseUrl;
            ProjectUrl = projectUrl;
            RequireLicenseAcceptance = requireLicenseAcceptance;
            DevelopmentDependency = developmentDependency;
            Description = description;
            Summary = summary;
            ReleaseNotes = releaseNotes;
            Language = language;
            Tags = tags;
            Copyright = copyright;
            Dependencies = dependencies;
            MinClientVersion = minClientVersion;
            ReportAbuseUrl = reportAbuseUrl;
            DownloadCount = downloadCount;
            SupportedFrameworks = supportedFrameworks;
            IsAbsoluteLatestVersion = isAbsoluteLatestVersion;
            IsLatestVersion = isLatestVersion;
            Listed = listed;
            Published = published;
            PackageSize = packageSize;
            PackageHash = packageHash;
            PackageHashAlgorithm = packageHashAlgorithm;
            LastUpdated = lastUpdated;
            Created = created;
            Path = path;
            FullPath = fullPath;

            // Preload collections
            DependencySets.Any();
            SupportedFrameworks.Any();
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

            _dependencySets = package.DependencySets.ToList();
            Dependencies = DependencySetsAsString(package.DependencySets);

            _supportedFrameworks = package.GetSupportedFrameworks().ToList();
            SupportedFrameworks = string.Join("|", package.GetSupportedFrameworks().Select(VersionUtility.GetFrameworkString));

            PackageSize = packageDerivedData.PackageSize;
            PackageHash = packageDerivedData.PackageHash;
            PackageHashAlgorithm = packageDerivedData.PackageHashAlgorithm;
            LastUpdated = packageDerivedData.LastUpdated;
            Created = packageDerivedData.Created;
            Path = packageDerivedData.Path;
            FullPath = packageDerivedData.FullPath;
        }

        public string Id { get; set; }

        [JsonConverter(typeof(SemanticVersionJsonConverter))]
        public SemanticVersion Version { get; set; }

        public string Title { get; set; }

        public IEnumerable<string> Authors { get; set; }

        public IEnumerable<string> Owners { get; set; }

        public Uri IconUrl { get; set; }

        public Uri LicenseUrl { get; set; }

        public Uri ProjectUrl { get; set; }

        public bool RequireLicenseAcceptance { get; set; }

        public bool DevelopmentDependency { get; set; }

        public string Description { get; set; }

        public string Summary { get; set; }

        public string ReleaseNotes { get; set; }

        public string Language { get; set; }

        public string Tags { get; set; }

        public string Copyright { get; set; }

        public string Dependencies { get; set; }

        private List<PackageDependencySet> _dependencySets;

        [JsonIgnore]
        public IEnumerable<PackageDependencySet> DependencySets
        {
            get
            {
                if (String.IsNullOrEmpty(Dependencies))
                {
                    return Enumerable.Empty<PackageDependencySet>();
                }

                if (_dependencySets == null)
                {
                    _dependencySets = ParseDependencySet(Dependencies);
                }

                return _dependencySets;
            }
        }

        [JsonConverter(typeof(SemanticVersionJsonConverter))]
        public Version MinClientVersion { get; set; }

        public Uri ReportAbuseUrl { get; set; }

        public int DownloadCount { get; set; }
        
        public string SupportedFrameworks { get; set; }
       
        private List<FrameworkName> _supportedFrameworks;
        public IEnumerable<FrameworkName> GetSupportedFrameworks()
        {
            if (String.IsNullOrEmpty(SupportedFrameworks))
            {
                return Enumerable.Empty<FrameworkName>();
            }

            if (_supportedFrameworks == null)
            {
                var supportedFrameworksAsStrings = SupportedFrameworks.Split('|').ToList();

                _supportedFrameworks = supportedFrameworksAsStrings
                    .Select(VersionUtility.ParseFrameworkName)
                    .ToList();
            }

            return _supportedFrameworks;
        }

        public bool IsAbsoluteLatestVersion { get; set; }

        public bool IsLatestVersion { get; set; }

        public bool Listed { get; set; }

        public DateTimeOffset? Published { get; set; }

        
        public long PackageSize { get; set; }

        public string PackageHash { get; set; }

        public string PackageHashAlgorithm { get; set; }

        public DateTimeOffset LastUpdated { get; set; }

        public DateTimeOffset Created { get; set; }

        public string Path { get; set; }

        public string FullPath { get; set; }

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
        
        [JsonIgnore]
        public IEnumerable<FrameworkAssemblyReference> FrameworkAssemblies
        {
            get
            {
                throw new NotImplementedException("The NuGet.Server.ServerPackage type does not support enumerating FrameworkAssemblies.");
            }
        }

        [JsonIgnore]
        public ICollection<PackageReferenceSet> PackageAssemblyReferences
        {
            get
            {
                throw new NotImplementedException("The NuGet.Server.ServerPackage type does not support enumerating PackageAssemblyReferences.");
            }
        }

        [JsonIgnore]
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
