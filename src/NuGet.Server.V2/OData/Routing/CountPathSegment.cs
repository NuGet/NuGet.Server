// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Copied from NuGetGallery (commit:f2fc834d 26.05.2016).

using System.Web.Http.OData.Routing;
using Microsoft.Data.Edm;
using Microsoft.Data.Edm.Library;

namespace NuGet.Server.V2.OData.Routing
{
    public class CountPathSegment
        : ODataPathSegment
    {
        public override string SegmentKind
        {
            get { return "$count"; }
        }

        public override IEdmType GetEdmType(IEdmType previousEdmType)
        {
            return EdmCoreModel.Instance.FindDeclaredType("Edm.Int32");
        }

        public override IEdmEntitySet GetEntitySet(IEdmEntitySet previousEntitySet)
        {
            return previousEntitySet;
        }

        public override string ToString()
        {
            return "$count";
        }
    }
}