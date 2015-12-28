﻿using System.Data.Services;
using System.ServiceModel.Activation;
using System.Web.Routing;
using NuGet.Server;
using NuGet.Server.DataServices;

[assembly: WebActivatorEx.PreApplicationStartMethod(typeof($rootnamespace$.NuGetRoutes), "Start")]

namespace $rootnamespace$ {
    public static class NuGetRoutes {
        public static void Start() {
			ServiceResolver.SetServiceResolver(new DefaultServiceResolver());

            MapRoutes(RouteTable.Routes);
        }

        private static void MapRoutes(RouteCollection routes) {
            // The default route is http://{root}/nuget/Packages
            var factory = new DataServiceHostFactory();
            var serviceRoute = new ServiceRoute("nuget", factory, typeof(Packages));
            serviceRoute.Defaults = new RouteValueDictionary { { "serviceType", "odata" } };
            serviceRoute.Constraints = new RouteValueDictionary { { "serviceType", "odata" } };
            routes.Add("nuget", serviceRoute);
        }
    }
}
