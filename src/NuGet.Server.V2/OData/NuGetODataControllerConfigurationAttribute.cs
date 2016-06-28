using NuGet.Server.V2.OData.Serializers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.OData.Formatter;
using System.Web.Http.OData.Formatter.Deserialization;


namespace NuGet.Server.V2.OData
{
    class NuGetODataControllerConfigurationAttribute : Attribute, IControllerConfiguration
    {
        public void Initialize(HttpControllerSettings controllerSettings, HttpControllerDescriptor controllerDescriptor)
        {
            var serProvider = new CustomSerializerProvider(provider => new NuGetEntityTypeSerializer(provider));
            var formatters = ODataMediaTypeFormatters.Create(serProvider, new DefaultODataDeserializerProvider());

            controllerSettings.Formatters.Clear();
            controllerSettings.Formatters.InsertRange(0, formatters);
        }
    }
}
