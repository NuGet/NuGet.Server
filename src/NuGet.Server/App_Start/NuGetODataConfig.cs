using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using NuGet.Server.V2;
using System.Web.Http.Routing;

[assembly: WebActivatorEx.PreApplicationStartMethod(typeof(NuGet.Server.App_Start.NuGetODataConfig), "Start")]
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
