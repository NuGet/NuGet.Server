// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Configuration;
using System.Web.Hosting;

namespace NuGet.Server.Infrastructure
{
    public class PackageUtility
    {
        private static readonly Lazy<string> _packagePhysicalPath = new Lazy<string>(ResolvePackagePath);

        public static string PackagePhysicalPath
        {
            get
            {
                return _packagePhysicalPath.Value;
            }
        }

        private static string ResolvePackagePath()
        {
            // The packagesPath could be an absolute path (rooted and use as is)
            // or a virtual path (and use as a virtual path)
            var path = ConfigurationManager.AppSettings["packagesPath"];

            if (String.IsNullOrEmpty(path))
            {
                // Default path
                return HostingEnvironment.MapPath("~/Packages");
            }

            if (path.StartsWith("~/"))
            {
                return HostingEnvironment.MapPath(path);
            }

            return path;
        }
    }
}
