using Microsoft.Data.OData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.OData;
using System.Web.Http.OData.Formatter.Serialization;

namespace NuGet.Server.V2.OData.Serialization
{
    abstract class StreamAwareEntityTypeSerializer<T> : ODataEntityTypeSerializer where T : class
    {
        protected StreamAwareEntityTypeSerializer(ODataSerializerProvider serializerProvider)
            : base(serializerProvider)
        {
        }

        public override ODataEntry CreateEntry(SelectExpandNode selectExpandNode, EntityInstanceContext entityInstanceContext)
        {
            var entry = base.CreateEntry(selectExpandNode, entityInstanceContext);

            var instance = entityInstanceContext.EntityInstance as T;

            if (instance != null)
            {
                entry.MediaResource = new ODataStreamReferenceValue
                {
                    ContentType = ContentType,
                    ReadLink = BuildLinkForStreamProperty(instance, entityInstanceContext)
                };
            }
            return entry;
        }

        public virtual string ContentType
        {
            get { return "application/octet-stream"; }
        }

        public abstract Uri BuildLinkForStreamProperty(T entity, EntityInstanceContext entityInstanceContext);
    }
}
