using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;

namespace NuGet.Server.V2.Samples.OwinHost.Controllers
{
    /// <summary>
    /// Controller that exposes Program.NuGetPublicRepository as NuGet OData feed
    /// that allows unauthenticated read/download access.
    /// Delete/upload requires user supplied ApiKey to match Program.ApiKey.
    /// </summary>
    public class NuGetPublicODataController : NuGetODataController
    {
        public NuGetPublicODataController()
            : base(Program.NuGetPublicRepository, new ApiKeyPackageAuthenticationService(true,Program.ApiKey))
        {

        }
    }
}
