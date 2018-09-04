// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 
using System;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.OData.Builder;
using System.Web.Http.OData.Extensions;
using System.Web.Http.OData.Routing.Conventions;
using System.Web.Http.Routing;
using Microsoft.Data.Edm;
using Microsoft.Data.OData;
using NuGet.Server.Core.DataServices;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.OData.Conventions;
using NuGet.Server.V2.OData.Routing;

namespace NuGet.Server.V2
{
    public static class NuGetV2WebApiEnabler
    {
        public static HttpConfiguration UseNuGetV2WebApiFeed(this HttpConfiguration config,
            string routeName,
            string routeUrlRoot, 
            string oDatacontrollerName)
        {
            // Insert conventions to make NuGet-compatible OData feed possible
            var conventions = ODataRoutingConventions.CreateDefault();
            conventions.Insert(0, new EntitySetCountRoutingConvention());
            conventions.Insert(0, new ActionCountRoutingConvention(oDatacontrollerName));
            conventions.Insert(0, new MethodNameActionRoutingConvention(oDatacontrollerName));
            conventions.Insert(0, new EntitySetPropertyRoutingConvention(oDatacontrollerName));
            conventions.Insert(0, new CompositeKeyRoutingConvention());

            // Translate all requests to Packages to use specified controllername instead of PackagesController
            conventions = conventions.Select(c => new ControllerAliasingODataRoutingConvention(c, "Packages", oDatacontrollerName))
                            .Cast<IODataRoutingConvention>()
                            .ToList();

            var oDataModel = BuildNuGetODataModel();

            config.Routes.MapHttpRoute(
                 name: routeName + "_upload",
                 routeTemplate: routeUrlRoot + "/",
                 defaults: new { controller = oDatacontrollerName, action = "UploadPackage" },
                 constraints: new { httpMethod = new HttpMethodConstraint(HttpMethod.Put) }
             );

            config.Routes.MapHttpRoute(
                 name: routeName + "_delete",
                 routeTemplate: routeUrlRoot + "/{id}/{version}",
                 defaults: new { controller = oDatacontrollerName, action = "DeletePackage" },
                 constraints: new { httpMethod = new HttpMethodConstraint(HttpMethod.Delete) }
             );

            config.Routes.MapHttpRoute(
                 name: "apiv2package_upload",
                 routeTemplate: "api/v2/package",
                 defaults: new { controller = oDatacontrollerName, action = "UploadPackage" },
                 constraints: new { httpMethod = new HttpMethodConstraint(HttpMethod.Put) }
             );

            config.Routes.MapODataServiceRoute(routeName, routeUrlRoot, oDataModel, new CountODataPathHandler(), conventions);
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

            builder.DataServiceVersion = new Version(2, 0);
            builder.MaxDataServiceVersion = builder.DataServiceVersion;

            var packagesCollection = builder.EntitySet<ODataPackage>("Packages");
            packagesCollection.EntityType.HasKey(pkg => pkg.Id);
            packagesCollection.EntityType.HasKey(pkg => pkg.Version);

            var downloadPackageAction = packagesCollection.EntityType.Action("Download");

            var searchAction = builder.Action("Search");
            searchAction.Parameter<string>("searchTerm");
            searchAction.Parameter<string>("targetFramework");
            searchAction.Parameter<bool>("includePrerelease");
            searchAction.ReturnsCollectionFromEntitySet(packagesCollection);

            var findPackagesAction = builder.Action("FindPackagesById");
            findPackagesAction.Parameter<string>("id");
            findPackagesAction.ReturnsCollectionFromEntitySet(packagesCollection);

            var getUpdatesAction = builder.Action("GetUpdates");
            getUpdatesAction.Parameter<string>("packageIds");
            getUpdatesAction.Parameter<string>("versions");
            getUpdatesAction.Parameter<bool>("includePrerelease");
            getUpdatesAction.Parameter<bool>("includeAllVersions");
            getUpdatesAction.Parameter<string>("targetFrameworks");
            getUpdatesAction.Parameter<string>("versionConstraints");
            getUpdatesAction.ReturnsCollectionFromEntitySet(packagesCollection);

            var retValue = builder.GetEdmModel();
            retValue.SetHasDefaultStream(retValue.FindDeclaredType(typeof(ODataPackage).FullName) as IEdmEntityType, hasStream: true);
            return retValue;
        }

    }

}
