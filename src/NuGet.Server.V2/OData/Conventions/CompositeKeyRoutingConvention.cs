// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Copied from NuGetGallery (commit:f2fc834d 26.05.2016). Modified to support entityactions with composite keys.

using Microsoft.Data.Edm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
            var routeValues = controllerContext.RouteData.Values;

            var action = base.SelectAction(odataPath, controllerContext, actionMap);
            if (action != null)
            {
                if (routeValues.ContainsKey(ODataRouteConstants.Key))
                {
                    var keyRaw = routeValues[ODataRouteConstants.Key] as string;
                    if (keyRaw != null)
                    {
                        CompositeODataKeyHelper.TryEnrichRouteValues(keyRaw, routeValues);
                    }
                }
            }
            //Allows actions for an entity with composite key
            else if (odataPath.PathTemplate == "~/entityset/key/action" ||
                    odataPath.PathTemplate == "~/entityset/key/cast/action")
            {
                KeyValuePathSegment keyValueSegment = odataPath.Segments[1] as KeyValuePathSegment;
                ActionPathSegment actionSegment = odataPath.Segments.Last() as ActionPathSegment;
                var actionFunctionImport = actionSegment.Action;

                controllerContext.RouteData.Values[ODataRouteConstants.Key] = keyValueSegment.Value;
                CompositeODataKeyHelper.TryEnrichRouteValues(keyValueSegment.Value, routeValues);
                return actionFunctionImport.Name;
            }

            return action;
        }
    }
}
