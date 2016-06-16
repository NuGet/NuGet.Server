using NuGet.Server.Core.DataServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.OData;

namespace NuGet.Server.V2.OData
{
    interface IDownloadLinkProvider
    {
        Uri GetDownloadUrl(ODataPackage package, EntityInstanceContext context);
    }
}
