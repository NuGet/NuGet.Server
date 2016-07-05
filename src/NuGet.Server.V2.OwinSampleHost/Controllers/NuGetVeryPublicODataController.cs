using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;

namespace NuGet.Server.V2.OwinSampleHost
{
    public class NuGetVeryPublicODataController : NuGetODataController
    {
        static IPackageAuthenticationService CreateAuthenticationService()
        {
            //Allows write access without apikey
            return new ApiKeyPackageAuthenticationService(false, null);
        }

        public NuGetVeryPublicODataController()
            : base(Program.NuGetVeryPublicRepository, CreateAuthenticationService())
        {           
           
        }

        public override HttpResponseMessage DeletePackage(string id, string version)
        {
            return base.DeletePackage(id, version);
        }
    }
}
