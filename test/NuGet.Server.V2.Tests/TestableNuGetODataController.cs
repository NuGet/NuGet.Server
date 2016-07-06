using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Server.V2.Tests
{
    class TestableNuGetODataController : NuGetODataController
    {
        public TestableNuGetODataController(IServerPackageRepository serverPackageRepository)
            :base(serverPackageRepository, null)
        {

        }
    }
}
