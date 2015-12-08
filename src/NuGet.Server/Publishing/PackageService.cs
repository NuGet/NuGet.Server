using System;
using System.IO;
using System.Net;
using System.Web;
using System.Web.Routing;
using NuGet.Server.DataServices;
using NuGet.Server.Infrastructure;

namespace NuGet.Server
{
    public class PackageService : IPackageService
    {
        private const string ApiKeyHeader = "X-NUGET-APIKEY";
        private readonly IServerPackageRepository _serverRepository;
        private readonly IPackageAuthenticationService _authenticationService;

        public PackageService(IServerPackageRepository repository,
                              IPackageAuthenticationService authenticationService)
        {
            _serverRepository = repository;
            _authenticationService = authenticationService;
        }

        public void CreatePackage(HttpContextBase context)
        {
            var request = context.Request;

            // Get the api key from the header
            string apiKey = request.Headers[ApiKeyHeader];

            // Get the package from the request body
            Stream stream = request.Files.Count > 0 ? request.Files[0].InputStream : request.InputStream;

            var package = new ZipPackage(stream);

            // Make sure they can access this package
            if (Authenticate(context, apiKey, package.Id))
            {
                try
                {
                    _serverRepository.AddPackage(package);
                    WriteStatus(context, HttpStatusCode.Created, "");
                }
                catch (InvalidOperationException ex)
                {
                    WriteStatus(context, HttpStatusCode.InternalServerError, ex.Message);
                }
            }
        }

        public void PublishPackage(HttpContextBase context)
        {
            // No-op
        }

        public void DeletePackage(HttpContextBase context)
        {
            RouteData routeData = GetRouteData(context);

            // Extract the apiKey, packageId and make sure the version if a valid version string
            // (fail to parse if it's not)
            string apiKey = context.Request.Headers[ApiKeyHeader];
            string packageId = routeData.GetRequiredString("packageId");
            var version = new SemanticVersion(routeData.GetRequiredString("version"));

            IPackage requestedPackage = _serverRepository.FindPackage(packageId, version);

            if (requestedPackage == null || ! requestedPackage.Listed)
            {
                // Package not found
                WritePackageNotFound(context, packageId, version);
            }
            else if (Authenticate(context, apiKey, packageId)) 
            {
                _serverRepository.RemovePackage(packageId, version);
            }
        }

        public void DownloadPackage(HttpContextBase context)
        {
            RouteData routeData = GetRouteData(context);
            // Get the package file name from the route
            string packageId = routeData.GetRequiredString("packageId");
            var version = new SemanticVersion(routeData.GetRequiredString("version"));

            IPackage requestedPackage = _serverRepository.FindPackage(packageId, version);

            if (requestedPackage != null)
            {
                Package packageMetatada = _serverRepository.GetMetadataPackage(requestedPackage);
                context.Response.AddHeader("content-disposition", String.Format("attachment; filename={0}", packageMetatada.Path));
                context.Response.ContentType = "application/zip";
                context.Response.TransmitFile(packageMetatada.FullPath);
            }
            else
            {
                // Package not found
                WritePackageNotFound(context, packageId, version);
            }
        }

        private static void WritePackageNotFound(HttpContextBase context, string packageId, SemanticVersion version)
        {
            WriteStatus(context, HttpStatusCode.NotFound, String.Format("'Package {0} {1}' Not found.", packageId, version));
        }

        private bool Authenticate(HttpContextBase context, string apiKey, string packageId)
        {
            if (_authenticationService.IsAuthenticated(context.User, apiKey, packageId))
            {
                return true;
            }
            else
            {
                WriteForbidden(context, packageId);
                return false;
            }
        }

        private static void WriteForbidden(HttpContextBase context, string packageId)
        {
            WriteStatus(context, HttpStatusCode.Forbidden, String.Format("Access denied for package '{0}'.", packageId));
        }

        private static void WriteStatus(HttpContextBase context, HttpStatusCode statusCode, string body = null)
        {
            context.Response.StatusCode = (int)statusCode;
            if (!String.IsNullOrEmpty(body))
            {
                context.Response.StatusDescription = body;
            }
        }

        private RouteData GetRouteData(HttpContextBase context)
        {
            return RouteTable.Routes.GetRouteData(context);
        }
    }
}