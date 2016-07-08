// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.Controllers;

namespace NuGet.Server.V2.Samples.OwinHost.Controllers
{
    /// <summary>
    /// Controller that exposes Program.NuGetPublicRepository as NuGet OData feed
    /// that allows unauthenticated read/download access.
    /// Delete/upload is allowed without authentication or apikey.
    /// </summary>
    public class NuGetVeryPublicODataController : NuGetODataController
    {
        static IPackageAuthenticationService CreateAuthenticationService()
        {
            //Allows write access without apikey
            return new ApiKeyPackageAuthenticationService(false, null);
        }

        public NuGetVeryPublicODataController()
            : base(Program.NuGetVeryPublicRepository, CreateAuthenticationService())
        {           
           
        }

        //Uncomment lines below to only allow delete for authorized users in Admin role
        //[Authorize(Roles = "Admin")]
        //public override Task<HttpResponseMessage> UploadPackage()
        //{
        //    return base.UploadPackage();
        //}

        //Uncomment lines below to only allow delete for authorized users in Admin role
        //[Authorize(Roles = "Admin")]
        //public override HttpResponseMessage DeletePackage(string id, string version)
        //{
        //    return base.DeletePackage(id, version);
        //}
    }
}
