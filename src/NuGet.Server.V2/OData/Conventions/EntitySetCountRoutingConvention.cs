// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Copied from NuGetGallery (commit:f2fc834d 26.05.2016).

using System.Linq;
using System.Net.Http;
using System.Web.Http.Controllers;
using System.Web.Http.OData.Routing;
using System.Web.Http.OData.Routing.Conventions;

namespace NuGet.Server.V2.OData.Conventions
{
    /// <summary>
    /// Maps /$count on an EntitySet to an action method called GetCount().
    /// </summary>
    public class EntitySetCountRoutingConvention 
        : EntitySetRoutingConvention
    {
        public override string SelectAction(ODataPath odataPath, HttpControllerContext controllerContext, ILookup<string, HttpActionDescriptor> actionMap)
        {
            if (controllerContext.Request.Method == HttpMethod.Get 
                && odataPath.PathTemplate == "~/entityset/$count"
                && actionMap.Contains("GetCount"))
            {
                return "GetCount";
            }
            return null;
        }
    }
}