// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Configuration;
using System.Web;
using System.Web.Hosting;
using System.Web.Routing;
using NuGet.Server.Core.DataServices;
using NuGet.Server.DataServices;

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

        public static string GetPackageDownloadUrl(ODataPackage package)
        {
            var routesValues = new RouteValueDictionary { 
                { "packageId", package.Id },
                { "version", package.Version } 
            };

            var context = HttpContext.Current;

            var route = RouteTable.Routes["DownloadPackage"];

            var vpd = route.GetVirtualPath(context.Request.RequestContext, routesValues);

            var applicationPath = Helpers.EnsureTrailingSlash(context.Request.ApplicationPath);

            return applicationPath + vpd.VirtualPath;
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
