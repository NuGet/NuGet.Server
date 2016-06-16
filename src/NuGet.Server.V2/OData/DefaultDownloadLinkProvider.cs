using NuGet.Server.Core.DataServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.OData;
using System.Web.Http.Routing;

namespace NuGet.Server.V2.OData
{
    class DefaultDownloadLinkProvider : IDownloadLinkProvider
    {
        string _downloadRouteName;

        public DefaultDownloadLinkProvider(string downloadRouteName)
        {
            _downloadRouteName = downloadRouteName;
        }

        public Uri GetDownloadUrl(ODataPackage package, EntityInstanceContext context)
        {
            var url = new UrlHelper(context.Request);
            var routeParams = new { package.Id, package.Version };
            var downloadLink = url.Link(_downloadRouteName, routeParams);
            return new Uri(downloadLink, UriKind.Absolute);
        }
    }

}
