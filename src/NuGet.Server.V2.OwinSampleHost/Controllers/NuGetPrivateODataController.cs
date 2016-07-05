using NuGet.Server.V2.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace NuGet.Server.V2.OwinSampleHost
{
    [Authorize]
    //Requires user to be authorized to use this feed.
    public class NuGetPrivateODataController : NuGetODataController
    {
        public NuGetPrivateODataController()
            : base(Program.NuGetPrivateRepository)
        {

        }
    }
}
