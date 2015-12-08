using System;
using System.Configuration;
using System.Web;
using System.Web.Hosting;
using System.Web.Routing;
using NuGet.Server.DataServices;

namespace NuGet.Server.Infrastructure
{
    public class PackageUtility
    {
        private static Lazy<string> _packagePhysicalPath = new Lazy<string>(ResolvePackagePath);

        public static string PackagePhysicalPath
        {
            get
            {
                return _packagePhysicalPath.Value;
            }
        }

        public static string GetPackageDownloadUrl(Package package)
        {
            var routesValues = new RouteValueDictionary { 
                { "packageId", package.Id },
                { "version", package.Version.ToString() } 
            };

            var context = HttpContext.Current;

            RouteBase route = RouteTable.Routes["DownloadPackage"];

            var vpd = route.GetVirtualPath(context.Request.RequestContext, routesValues);

            string applicationPath = Helpers.EnsureTrailingSlash(context.Request.ApplicationPath);

            return applicationPath + vpd.VirtualPath;
        }

        private static string ResolvePackagePath()
        {
            // The packagesPath could be an absolute path (rooted and use as is)
            // or a virtual path (and use as a virtual path)
            string path = ConfigurationManager.AppSettings["packagesPath"];

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
