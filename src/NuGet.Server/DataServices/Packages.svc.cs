// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.Data.Services;
using System.Data.Services.Common;
using System.Data.Services.Providers;
using System.IO;
using System.Linq;
using System.ServiceModel.Web;
using System.Web;
using NuGet.Server.Infrastructure;

namespace NuGet.Server.DataServices
{
    // Disabled for live service
    [System.ServiceModel.ServiceBehavior(IncludeExceptionDetailInFaults = true)]
    public class Packages : DataService<PackageContext>, IDataServiceStreamProvider, IServiceProvider
    {
        private IServerPackageRepository Repository
        {
            get
            {
                // It's bad to use the container directly but we aren't in the loop when this 
                // class is created
                return ServiceResolver.Resolve<IServerPackageRepository>();
            }
        }

        // This method is called only once to initialize service-wide policies.
        public static void InitializeService(DataServiceConfiguration config)
        {
            config.SetEntitySetAccessRule("Packages", EntitySetRights.AllRead);
            config.SetEntitySetPageSize("Packages", 100);
            config.DataServiceBehavior.MaxProtocolVersion = DataServiceProtocolVersion.V2;
            config.UseVerboseErrors = true;
            RegisterServices(config);
        }

        internal static void RegisterServices(IDataServiceConfiguration config)
        {
            config.SetServiceOperationAccessRule("Search", ServiceOperationRights.AllRead);
            config.SetServiceOperationAccessRule("FindPackagesById", ServiceOperationRights.AllRead);
            config.SetServiceOperationAccessRule("GetUpdates", ServiceOperationRights.AllRead);
        }

        protected override PackageContext CreateDataSource()
        {
            return new PackageContext(Repository);
        }

        public void DeleteStream(object entity, DataServiceOperationContext operationContext)
        {
            throw new NotSupportedException();
        }

        public Stream GetReadStream(object entity, string etag, bool? checkETagForEquality, DataServiceOperationContext operationContext)
        {
            throw new NotSupportedException();
        }

        public Uri GetReadStreamUri(object entity, DataServiceOperationContext operationContext)
        {
            var package = (ODataPackage)entity;

            var rootUrlConfig = System.Configuration.ConfigurationManager.AppSettings["rootUrl"];
            var rootUrl = !string.IsNullOrWhiteSpace(rootUrlConfig) 
                ? rootUrlConfig
                : HttpContext.Current.Request.Url.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped);

            // the URI need to ends with a '/' to be correctly merged so we add it to the application if it 
            var downloadUrl = PackageUtility.GetPackageDownloadUrl(package);
            return new Uri(new Uri(rootUrl), downloadUrl);
        }

        public string GetStreamContentType(object entity, DataServiceOperationContext operationContext)
        {
            return "application/zip";
        }

        public string GetStreamETag(object entity, DataServiceOperationContext operationContext)
        {
            return null;
        }

        public Stream GetWriteStream(object entity, string etag, bool? checkETagForEquality, DataServiceOperationContext operationContext)
        {
            throw new NotSupportedException();
        }

        public string ResolveType(string entitySetName, DataServiceOperationContext operationContext)
        {
            throw new NotSupportedException();
        }

        public int StreamBufferSize
        {
            get
            {
                return 64000;
            }
        }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IDataServiceStreamProvider))
            {
                return this;
            }
            return null;
        }

        [WebGet]
        public IQueryable<ODataPackage> Search(string searchTerm, string targetFramework, bool includePrerelease, bool? includeDelisted)
        {
            var targetFrameworks = String.IsNullOrEmpty(targetFramework) ? Enumerable.Empty<string>() : targetFramework.Split('|');

            return Repository
                .Search(searchTerm, targetFrameworks, includePrerelease)
                .Select(package => package.AsODataPackage())
                .AsQueryable();
        }

        [WebGet]
        public IQueryable<ODataPackage> FindPackagesById(string id)
        {
            return Repository
                .FindPackagesById(id)
                .Where(package => package.Listed)
                .Select(package => package.AsODataPackage())
                .AsQueryable();
        }

        [WebGet]
        public IQueryable<ODataPackage> GetUpdates(
            string packageIds, string versions, bool includePrerelease, bool includeAllVersions, string targetFrameworks, string versionConstraints)
        {
            if (String.IsNullOrEmpty(packageIds) || String.IsNullOrEmpty(versions))
            {
                return Enumerable.Empty<ODataPackage>().AsQueryable();
            }

            var idValues = packageIds.Trim().Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            var versionValues = versions.Trim().Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            var targetFrameworkValues = String.IsNullOrEmpty(targetFrameworks) ? null :
                                                                                 targetFrameworks.Split('|').Select(VersionUtility.ParseFrameworkName).ToList();
            var versionConstraintValues = String.IsNullOrEmpty(versionConstraints)
                                            ? new string[idValues.Length]
                                            : versionConstraints.Split('|');

            if (idValues.Length == 0 || idValues.Length != versionValues.Length || idValues.Length != versionConstraintValues.Length)
            {
                // Exit early if the request looks invalid
                return Enumerable.Empty<ODataPackage>().AsQueryable();
            }

            var packagesToUpdate = new List<IPackageMetadata>();
            for (var i = 0; i < idValues.Length; i++)
            {
                packagesToUpdate.Add(new PackageBuilder { Id = idValues[i], Version = new SemanticVersion(versionValues[i]) });
            }

            var versionConstraintsList = new IVersionSpec[versionConstraintValues.Length];
            for (var i = 0; i < versionConstraintsList.Length; i++)
            {
                if (!String.IsNullOrEmpty(versionConstraintValues[i]))
                {
                    VersionUtility.TryParseVersionSpec(versionConstraintValues[i], out versionConstraintsList[i]);
                }
            }

            return Repository
                .GetUpdatesCore(packagesToUpdate, includePrerelease, includeAllVersions, targetFrameworkValues, versionConstraintsList)
                .Select(package => package.AsODataPackage())
                .AsQueryable();
        }
    }
}
