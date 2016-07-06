using Microsoft.Owin.Hosting;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.Core.Logging;
using NuGet.Server.V2;
using Owin;
using System;
using System.Collections.Generic;
using System.Data.Services;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Web.Http;
using System.Web.Http.OData.Extensions;

namespace NuGet.Server.V2.Samples.OwinHost
{
    class Program
    {
        public static IServerPackageRepository NuGetPrivateRepository { get; private set; }
        public static IServerPackageRepository NuGetPublicRepository { get; private set; }
        public static IServerPackageRepository NuGetVeryPublicRepository { get; private set; }

        public const string ApiKey = "key123"; 

        static void Main(string[] args)
        {
            var baseAddress = "http://localhost:9000/";

            //Set up a common settingsProvider to be used by all repositories. 
            //If a setting is not present in dictionary default value will be used.
            var settings = new Dictionary<string, bool>();
            settings.Add("enableDelisting", false);                         //default=false
            settings.Add("enableFrameworkFiltering", false);                //default=false
            settings.Add("ignoreSymbolsPackages", true);                    //default=false
            settings.Add("allowOverrideExistingPackageOnPush", true);       //default=true
            var settingsProvider = new DictionarySettingsProvider(settings);

            var logger = new ConsoleLogger();

            //Sets up three repositories with seperate packages in each feed. These repositories are used by our controllers.
            //In a real world application the repositories will probably be inserted through DI framework, or created in the controllers constructor.
            NuGetPrivateRepository = NuGetV2WebApiEnabler.CreatePackageRepository(@"d:\omnishopcentraldata\Packages\Private", settingsProvider, logger);
            NuGetPublicRepository = NuGetV2WebApiEnabler.CreatePackageRepository(@"d:\omnishopcentraldata\Packages\Public", settingsProvider, logger);
            NuGetVeryPublicRepository = NuGetV2WebApiEnabler.CreatePackageRepository(@"d:\omnishopcentraldata\Packages\VeryPublic", settingsProvider, logger);

            // Start OWIN host, which in turn will create a new instance of Startup class, and execute its Configuration method.
            using (WebApp.Start<Startup>(url: baseAddress))
            {
                Console.WriteLine("Server listening at baseaddress: " + baseAddress);
                Console.WriteLine("[ENTER] to close server");
                Console.ReadLine();
            }
        }
    }

    public class Startup
    {
        public void Configuration(IAppBuilder appBuilder)
        {
            //Simple authenticator that authorizes all users that supply a username and password. Only meant for demonstration purposes.
            appBuilder.Use(typeof(BasicAuthentication));

            // Configure Web API for self-host. 
            HttpConfiguration config = new HttpConfiguration();
            appBuilder.UseWebApi(config);

            //Map route for ordinary controllers, this is not neccessary for the NuGet feed.
            //It is just included as an example of combining ordinary controllers with NuGet OData Controllers.
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            //Feed that allows  read/download access for authenticated users, delete/upload is disabled (configured in controller's constructor).
            //User authentication is done by hosting environment, typical Owin pipeline or IIS (configured by attribute on controller).
            NuGetV2WebApiEnabler.UseNuGetV2WebApiFeed(config,
                routeName: "NuGetAdmin",
                routeUrlRoot: "NuGet/admin",
                oDatacontrollerName: "NuGetPrivateOData");            //NuGetPrivateODataController.cs, located in Controllers\ folder

            //Feed that allows unauthenticated read/download access, delete/upload requires ApiKey (configured in controller's constructor).
            NuGetV2WebApiEnabler.UseNuGetV2WebApiFeed(config,
                routeName: "NuGetPublic",
                routeUrlRoot: "NuGet/public",
                oDatacontrollerName: "NuGetPublicOData");            //NuGetPublicODataController.cs, located in Controllers\ folder


            //Feed that allows unauthenticated read/download/delete/upload (configured in controller's constructor).
            NuGetV2WebApiEnabler.UseNuGetV2WebApiFeed(config,
                routeName: "NuGetVeryPublic",
                routeUrlRoot: "NuGet/verypublic",
                oDatacontrollerName: "NuGetVeryPublicOData");        //NuGetVeryPublicODataController.cs, located in Controllers\ folder

        }
    }
}
