// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Net.Http;
using System.Web.Http;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.Controllers;

namespace NuGet.Server.DataServices
{
    public class PackagesODataController : NuGetODataController
    {
        public PackagesODataController()
            : base(Repository, AuthenticationService)
        {
            _maxPageSize = 100;
        }

        private static IServerPackageRepository Repository
        {
            get
            {
                // It's bad to use the container directly but we aren't in the loop when this 
                // class is created
                return ServiceResolver.Resolve<IServerPackageRepository>();
            }
        }

        private static IPackageAuthenticationService AuthenticationService
        {
            get
            {
                // It's bad to use the container directly but we aren't in the loop when this 
                // class is created
                return ServiceResolver.Resolve<IPackageAuthenticationService>();
            }
        }

        [HttpGet]
        // Exposed through ordinary Web API route. Bypasses OData pipeline.
        public HttpResponseMessage ClearCache()
        {
            if (RequestContext.IsLocal)
            {
                _serverRepository.ClearCache();
                return CreateStringResponse(HttpStatusCode.OK, "Server cache has been cleared.");
            }
            else
            {
                return CreateStringResponse(HttpStatusCode.Forbidden, "Clear cache is only supported for local requests.");
            }
        }
    }
}