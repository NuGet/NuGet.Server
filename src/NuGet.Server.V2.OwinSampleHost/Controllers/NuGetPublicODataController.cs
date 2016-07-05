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
    public class NuGetPublicODataController : NuGetODataController
    {
        public NuGetPublicODataController()
            : base(Program.NuGetPublicRepository, new ApiKeyPackageAuthenticationService(true,Program.ApiKey))
        {

        }

        public override HttpResponseMessage Download(string id, string version = "")
        {
            return base.Download(id, version);
        }

        public override Task<HttpResponseMessage> UploadPackage()
        {
            return base.UploadPackage();
        }

        public override HttpResponseMessage DeletePackage(string id, string version)
        {
            return base.DeletePackage(id, version);
        }
    }
}
