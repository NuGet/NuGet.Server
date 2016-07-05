using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Net.Http;

namespace NuGet.Server.V2.OwinSampleHost
{
    /// <summary>
    /// Controller that exposes Program.NuGetPrivateRepository as NuGet OData feed
    /// that allows read/download access for all authenticated users.
    /// delete/upload is not allowed (no authenticationservice is passed to NuGetODataControllers constructor)
    /// </summary>
    [Authorize]
    public class NuGetPrivateODataController : NuGetODataController
    {
        public NuGetPrivateODataController()
            : base(Program.NuGetPrivateRepository)
            //Replace line above with the one below to allow upload/delete for all authenticated users
            //: base(Program.NuGetPrivateRepository, new ApiKeyPackageAuthenticationService(false, null))
        {
        }
    }
}
