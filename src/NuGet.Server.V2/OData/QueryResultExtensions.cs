// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Copied from NuGetGallery (commit:f2fc834d 26.05.2016).

using NuGet.Server.V2.Model;
using System.Web.Http;

namespace NuGet.Server.V2.OData
{
    public static class QueryResultExtensions
    {
        public static IHttpActionResult FormattedAsCountResult<T>(this IHttpActionResult current)
        {
            var queryResult = current as QueryResult<T>;
            if (queryResult != null)
            {
                queryResult.FormatAsCountResult = true;
                return queryResult;
            }

            return current;
        }

        public static IHttpActionResult FormattedAsSingleResult<T>(this IHttpActionResult current)
        {
            var queryResult = current as QueryResult<T>;
            if (queryResult != null)
            {
                queryResult.FormatAsSingleResult = true;
                return queryResult;
            }

            return current;
        }
    }
}