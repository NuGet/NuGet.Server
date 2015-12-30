// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.IO;
using System.Net;
using System.Web;
using System.Web.Routing;
using NuGet.Server.Infrastructure;

namespace NuGet.Server.Publishing
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
            var apiKey = request.Headers[ApiKeyHeader];

            // Get the package from the request body
            // ReSharper disable once PossibleNullReferenceException
            var stream = request.Files.Count > 0 ? request.Files[0].InputStream : request.InputStream;

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
            var routeData = GetRouteData(context);

            // Extract the apiKey, packageId and make sure the version if a valid version string
            // (fail to parse if it's not)
            var apiKey = context.Request.Headers[ApiKeyHeader];
            var packageId = routeData.GetRequiredString("packageId");
            var version = new SemanticVersion(routeData.GetRequiredString("version"));

            var requestedPackage = _serverRepository.FindPackage(packageId, version);

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
            var routeData = GetRouteData(context);
            // Get the package file name from the route
            var packageId = routeData.GetRequiredString("packageId");
            var version = new SemanticVersion(routeData.GetRequiredString("version"));

            var requestedPackage = _serverRepository.FindPackage(packageId, version);
            if (requestedPackage != null)
            {
                context.Response.AddHeader("content-disposition", 
                    string.Format("attachment; filename={0}.{1}.nupkg", requestedPackage.Id, requestedPackage.Version.ToNormalizedString()));
                context.Response.ContentType = "binary/octet-stream";

                var serverPackage = requestedPackage as ServerPackage;
                if (serverPackage != null && !string.IsNullOrEmpty(serverPackage.FullPath))
                {
                    // FullPath known - send the file as-is
                    context.Response.TransmitFile(serverPackage.FullPath);
                }
                else
                {
                    // FullPath unknown - stream it
                    using (var packageStream = requestedPackage.GetStream())
                    {
                        packageStream.CopyTo(context.Response.OutputStream);
                    }
                }
            }
            else
            {
                // Package not found
                WritePackageNotFound(context, packageId, version);
            }
        }

        public void ClearCache(HttpContextBase context)
        {
            if (context.Request.IsLocal)
            {
                // Clear cache
                _serverRepository.ClearCache();
                WriteStatus(context, HttpStatusCode.OK);
                using (var responseStreamWriter = new StreamWriter(context.Response.OutputStream))
                {
                    responseStreamWriter.Write("Server cache has been cleared.");
                }
            }
            else
            {
                // Forbidden
                WriteStatus(context, HttpStatusCode.Forbidden, "Clear cache is only supported for local requests.");
            }
        }

        private static void WritePackageNotFound(HttpContextBase context, string packageId, SemanticVersion version)
        {
            WriteStatus(context, HttpStatusCode.NotFound, string.Format("'Package {0} {1}' Not found.", packageId, version));
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
            WriteStatus(context, HttpStatusCode.Forbidden, string.Format("Access denied for package '{0}'.", packageId));
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