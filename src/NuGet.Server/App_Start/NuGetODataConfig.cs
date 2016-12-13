// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Routing;
using NuGet.Server.V2;

// The consuming project executes this logic with its own copy of this class. This is done with a .pp file that is
// added and transformed upon package install.
#if DEBUG
[assembly: WebActivatorEx.PreApplicationStartMethod(typeof(NuGet.Server.App_Start.NuGetODataConfig), "Start")]
#endif

namespace NuGet.Server.App_Start
{
    public static class NuGetODataConfig
    {
        public static void Start()
        {
            ServiceResolver.SetServiceResolver(new DefaultServiceResolver());

            var config = GlobalConfiguration.Configuration;

            NuGetV2WebApiEnabler.UseNuGetV2WebApiFeed(config, "NuGetDefault", "nuget", "PackagesOData");

            config.Routes.MapHttpRoute(
                name: "NuGetDefault_ClearCache",
                routeTemplate: "nuget/clear-cache",
                defaults: new { controller = "PackagesOData", action = "ClearCache" },
                constraints: new { httpMethod = new HttpMethodConstraint(HttpMethod.Get) }
            );

        }
    }
}
