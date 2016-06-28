using Microsoft.Data.Edm;
using Microsoft.Data.OData;
using NuGet.Server.Core.DataServices;
using NuGet.Server.V2.OData.Conventions;
using NuGet.Server.V2.OData.Serializers;
using NuGet.Server.V2.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Dispatcher;
using System.Web.Http.OData;
using System.Web.Http.OData.Extensions;
using System.Web.Http.OData.Formatter;
using System.Web.Http.OData.Formatter.Deserialization;
using System.Web.Http.OData.Formatter.Serialization;
using System.Web.Http.OData.Routing;
using System.Web.Http.OData.Routing.Conventions;
using System.Web.Http.Routing;
using System.Web.Http.OData.Builder;
using System.Web.Http.Controllers;
using System.Net.Http;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.Core.Logging;
using System.Net;
using NuGet.Server.V2.OData;

namespace NuGet.Server.V2
{
    public static class NuGetV2WebApiEnabler
    {
        public static HttpConfiguration UseNuGetV2WebApiFeed(this HttpConfiguration config,
            string routeName,
            string routeUrlRoot, 
            string oDatacontrollerName,
            string downloadControllerName)
        {
            // Insert conventions to make NuGet-compatible OData feed possible
            var conventions = ODataRoutingConventions.CreateDefault();
            //conventions.Insert(0, new EntitySetCountRoutingConvention());
            //conventions.Insert(0, new ActionCountRoutingConvention(oDatacontrollerName));
            conventions.Insert(0, new MethodNameActionRoutingConvention(oDatacontrollerName));
            conventions.Insert(0, new CompositeKeyRoutingConvention());

            // Translate all requests to use V2FeedController instead of PackagesController
            conventions = conventions.Select(c => new ControllerAliasingODataRoutingConvention(c, "Packages", oDatacontrollerName))
                            .Cast<IODataRoutingConvention>()
                            .ToList();


            var oDataModel = BuildNuGetODataModel();
            config.Routes.MapODataServiceRoute(routeName, routeUrlRoot, oDataModel, new DefaultODataPathHandler(), conventions);

            var downloadRouteName = routeName + "_download";
            var downloadRouteTemplate = routeUrlRoot + "/PackagesDownload(Id='{id}',Version='{version}')";
            NuGetEntityTypeSerializer.RegisterDownloadLinkProvider(oDataModel, new DefaultDownloadLinkProvider(downloadRouteName));
            config.Routes.MapHttpRoute(downloadRouteName, downloadRouteTemplate,  new { controller = downloadControllerName, action = "DownloadPackage", version = RouteParameter.Optional });

            return config;
        }

        public static IServerPackageRepository CreatePackageRepository(string packagePath, ISettingsProvider settingsProvider=null, NuGet.Server.Core.Logging.ILogger logger=null)
        {
            var hashProvider = new CryptoHashProvider(Core.Constants.HashAlgorithm);
            return new ServerPackageRepository(packagePath, hashProvider, settingsProvider, logger);          
        }

        internal static IEdmModel BuildNuGetODataModel()
        {
            var builder = new ODataConventionModelBuilder();

            var packagesCollection = builder.EntitySet<ODataPackage>("Packages");
            packagesCollection.EntityType.HasKey(pkg => pkg.Id);
            packagesCollection.EntityType.HasKey(pkg => pkg.Version);

            var searchAction = builder.Action("Search");
            searchAction.Parameter<string>("searchTerm");
            searchAction.Parameter<string>("targetFramework");
            searchAction.Parameter<bool>("includePrerelease");
            searchAction.ReturnsCollectionFromEntitySet<ODataPackage>("Packages");

            var findPackagesAction = builder.Action("FindPackagesById");
            findPackagesAction.Parameter<string>("id");
            findPackagesAction.ReturnsCollectionFromEntitySet<ODataPackage>("Packages");

            var getUpdatesAction = builder.Action("GetUpdates");
            getUpdatesAction.Parameter<string>("packageIds");
            getUpdatesAction.Parameter<string>("versions");
            getUpdatesAction.Parameter<bool>("includePrerelease");
            getUpdatesAction.Parameter<bool>("includeAllVersions");
            getUpdatesAction.Parameter<string>("targetFrameworks");
            getUpdatesAction.Parameter<string>("versionConstraints");
            getUpdatesAction.ReturnsCollectionFromEntitySet<ODataPackage>("Packages");

            var retValue = builder.GetEdmModel();
            retValue.SetHasDefaultStream(retValue.FindDeclaredType(typeof(ODataPackage).FullName) as IEdmEntityType, hasStream: true);
            return retValue;
        }

    }

}
