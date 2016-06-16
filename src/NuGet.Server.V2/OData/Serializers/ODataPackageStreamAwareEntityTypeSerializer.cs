using Microsoft.Data.Edm;
using Microsoft.Data.OData;
using NuGet.Server.Core.DataServices;
using NuGet.Server.V2.OData.Conventions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Dispatcher;
using System.Web.Http.OData;
using System.Web.Http.OData.Extensions;
using System.Web.Http.OData.Formatter;
using System.Web.Http.OData.Formatter.Deserialization;
using System.Web.Http.OData.Formatter.Serialization;
using System.Web.Http.OData.Routing;
using System.Web.Http.OData.Routing.Conventions;
using System.Web.Http.Routing;

namespace NuGet.Server.V2.OData.Serialization
{
    class ODataPackageStreamAwareEntityTypeSerializer : StreamAwareEntityTypeSerializer<ODataPackage>
    {

        static Dictionary<IEdmModel, IDownloadLinkProvider> _downloadLinkProviders = new Dictionary<IEdmModel, IDownloadLinkProvider>();

        internal static void RegisterDownloadLinkProvider(IEdmModel oDataModel, IDownloadLinkProvider downloadLinkProvider)
        {
            _downloadLinkProviders.Add(oDataModel, downloadLinkProvider);
        }

        public ODataPackageStreamAwareEntityTypeSerializer(ODataSerializerProvider serializerProvider) : base(serializerProvider)
        {
        }

        public override Uri BuildLinkForStreamProperty(ODataPackage package, EntityInstanceContext context)
        {
            var linkProvider = _downloadLinkProviders[context.EdmModel];
            var retValue = linkProvider.GetDownloadUrl(package, context);
            return retValue;
        }

        public override string ContentType
        {
            get { return "application/zip"; }
        }
    }

}
