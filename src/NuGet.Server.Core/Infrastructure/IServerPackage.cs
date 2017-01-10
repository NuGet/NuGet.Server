// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace NuGet.Server.Core.Infrastructure
{
    public interface IServerPackage
    {
        string Id { get; }
        SemanticVersion Version { get; }
        string Title { get; }
        IEnumerable<string> Authors { get; }
        IEnumerable<string> Owners { get; }
        Uri IconUrl { get; }
        Uri LicenseUrl { get; }
        Uri ProjectUrl { get; }
        int DownloadCount { get; }
        bool RequireLicenseAcceptance { get; }
        bool DevelopmentDependency { get; }
        string Description { get; }
        string Summary { get; }
        string ReleaseNotes { get; }
        IEnumerable<PackageDependencySet> DependencySets { get; }
        string Copyright { get; }
        string Tags { get; }
        bool SemVer1IsAbsoluteLatest { get; }
        bool SemVer1IsLatest { get; }
        bool SemVer2IsAbsoluteLatest { get; }
        bool SemVer2IsLatest { get; }
        bool Listed { get; }
        Version MinClientVersion { get; }
        string Language { get; }
        long PackageSize { get; }
        string PackageHash { get; }
        string PackageHashAlgorithm { get; }
        string FullPath { get; }
        DateTimeOffset LastUpdated { get; }
        DateTimeOffset Created { get; }

        IEnumerable<FrameworkName> GetSupportedFrameworks();
    }
}
