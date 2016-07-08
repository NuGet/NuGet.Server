// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Web.Http.Controllers;
using System.Web.Http.OData.Formatter;
using System.Web.Http.OData.Formatter.Deserialization;
using NuGet.Server.V2.OData.Serializers;

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
