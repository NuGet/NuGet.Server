using Microsoft.Data.Edm;
using Microsoft.Data.OData;
using NuGet.Server.DataServices;
using NuGet.Server.V2.OData.Conventions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    class ODataPackageStreamAwareSerializerProvider : DefaultODataSerializerProvider
    {
        private readonly ODataEdmTypeSerializer entitySerializer;

        public ODataPackageStreamAwareSerializerProvider()
        {
            this.entitySerializer = new ODataPackageStreamAwareEntityTypeSerializer(this);
        }

        public override ODataEdmTypeSerializer GetEdmTypeSerializer(IEdmTypeReference edmType)
        {
            if (edmType.IsEntity())
            {
                return entitySerializer;
            }

            return base.GetEdmTypeSerializer(edmType);
        }
    }
}
