// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Copied from NuGetGallery (commit:f2fc834d 26.05.2016).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.OData.Routing;
using System.Web.Http.OData.Routing.Conventions;

namespace NuGet.Server.V2.OData.Conventions
{
    /// <summary>
    /// Adds support for composite keys in OData requests (e.g. (Id='',Version=''))
    /// </summary>
    public class CompositeKeyRoutingConvention
        : EntityRoutingConvention
    {
        public override string SelectAction(ODataPath odataPath, HttpControllerContext controllerContext, ILookup<string, HttpActionDescriptor> actionMap)
        {
            var action = base.SelectAction(odataPath, controllerContext, actionMap);
            if (action != null)
            {
                var routeValues = controllerContext.RouteData.Values;
                if (routeValues.ContainsKey(ODataRouteConstants.Key))
                {
                    var keyRaw = routeValues[ODataRouteConstants.Key] as string;
                    if (keyRaw != null)
                    {
                        if (!CompositeODataKeyHelper.TryEnrichRouteValues(keyRaw, routeValues))
                        {
                            return action;
                        }
                    }
                }
            }

            return action;
        }
    }
}
