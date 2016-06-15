using NuGet.Server.V2.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Server.V2.OWinSampleHost
{
    public class NuGetPublicDownloadController : NuGetDownloadController
    {
        public NuGetPublicDownloadController()
            : base(Program.NuGetPublicRepository)
        {

        }
    }
}
