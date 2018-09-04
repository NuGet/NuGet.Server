// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.Controllers;

namespace NuGet.Server.DataServices
{
    public class PackagesODataController : NuGetODataController
    {
        public PackagesODataController()
            : this(ServiceResolver.Current)
        {
        }

        protected PackagesODataController(IServiceResolver serviceResolver)
            : base(serviceResolver.Resolve<IServerPackageRepository>(),
                   serviceResolver.Resolve<IPackageAuthenticationService>())
        {
            _maxPageSize = 100;
        }

        [HttpGet]
        // Exposed through ordinary Web API route. Bypasses OData pipeline.
        public async Task<HttpResponseMessage> ClearCache(CancellationToken token)
        {
            if (RequestContext.IsLocal || ServiceResolver.Current.Resolve<ISettingsProvider>().GetBoolSetting("allowRemoteCacheManagement", false))
            {
                await _serverRepository.ClearCacheAsync(token);
                return CreateStringResponse(HttpStatusCode.OK, "Server cache has been cleared.");
            }
            else
            {
                return CreateStringResponse(HttpStatusCode.Forbidden, "Clear cache is only supported for local requests.");
            }
        }
    }
}