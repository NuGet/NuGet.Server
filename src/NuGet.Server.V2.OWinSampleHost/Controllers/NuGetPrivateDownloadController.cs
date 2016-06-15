using NuGet.Server.V2.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace NuGet.Server.V2.OWinSampleHost
{
    //[Authorize(Roles ="Admin")]
    public class NuGetPrivateDownloadController : NuGetDownloadController
    {
        public NuGetPrivateDownloadController()
            : base(Program.NuGetPrivateRepository)
        {

        }
    }
}
