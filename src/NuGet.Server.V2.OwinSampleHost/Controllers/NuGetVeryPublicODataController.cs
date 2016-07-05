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
    /// <summary>
    /// Controller that exposes Program.NuGetPublicRepository as NuGet OData feed
    /// that allows unauthenticated read/download access.
    /// Delete/upload is allowed without authentication or apikey.
    /// </summary>
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

        //Uncomment lines below to only allow delete for authorized users in Admin role
        //[Authorize(Roles = "Admin")]
        //public override Task<HttpResponseMessage> UploadPackage()
        //{
        //    return base.UploadPackage();
        //}

        //Uncomment lines below to only allow delete for authorized users in Admin role
        //[Authorize(Roles = "Admin")]
        //public override HttpResponseMessage DeletePackage(string id, string version)
        //{
        //    return base.DeletePackage(id, version);
        //}
    }
}
