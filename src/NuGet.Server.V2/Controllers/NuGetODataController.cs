// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.OData;
using System.Web.Http.OData.Query;
using NuGet.Server.Core.DataServices;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.Model;
using NuGet.Server.V2.OData;

namespace NuGet.Server.V2.Controllers
{
    [NuGetODataControllerConfiguration]
    public abstract class NuGetODataController : ODataController
    {
        const string ApiKeyHeader = "X-NUGET-APIKEY";

        protected int _maxPageSize = 25;

        protected readonly IServerPackageRepository _serverRepository;
        protected readonly IPackageAuthenticationService _authenticationService;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="repository">Required.</param>
        /// <param name="authenticationService">Optional. If this is not supplied Upload/Delete is not available (requests returns 403 Forbidden)</param>
        protected NuGetODataController(IServerPackageRepository repository, IPackageAuthenticationService authenticationService=null)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            _serverRepository = repository;
            _authenticationService = authenticationService;
        }
        
        // GET /Packages
        // Never seen this invoked. NuGet.Exe and Visual Studio seems to use 'Search' for all package listing.
        // Probably required to be OData compliant?
        [HttpGet]
        [EnableQuery(PageSize = 100, HandleNullPropagation = HandleNullPropagationOption.False)]
        public virtual IHttpActionResult Get(ODataQueryOptions<ODataPackage> options)
        {
            var sourceQuery = _serverRepository.GetPackages();
            return TransformToQueryResult(options, sourceQuery);
        }

        // GET /Packages/$count
        [HttpGet]
        public virtual IHttpActionResult GetCount(ODataQueryOptions<ODataPackage> options)
        {
            return (Get(options)).FormattedAsCountResult<ODataPackage>();
        }

        // GET /Packages(Id=,Version=)
        [HttpGet]
        public virtual IHttpActionResult Get(ODataQueryOptions<ODataPackage> options, string id, string version)
        {
            var package = RetrieveFromRepository(id, version);

            if (package == null)
                return NotFound();

            var queryable = (new[] { package.AsODataPackage() }).AsQueryable();
            var queryResult = QueryResult(options, queryable, _maxPageSize);
            return queryResult.FormattedAsSingleResult<ODataPackage>();
        }

        // GET/POST /FindPackagesById()?id=
        [HttpGet]
        [HttpPost]
        public virtual IHttpActionResult FindPackagesById(ODataQueryOptions<ODataPackage> options, [FromODataUri]string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                var emptyResult = Enumerable.Empty<ODataPackage>().AsQueryable();
                return QueryResult(options, emptyResult, _maxPageSize);
            }

            var sourceQuery = _serverRepository.FindPackagesById(id);
            return TransformToQueryResult(options, sourceQuery);
        }


        // GET /Packages(Id=,Version=)/propertyName
        [HttpGet]
        public virtual IHttpActionResult GetPropertyFromPackages(string propertyName, string id, string version)
        {
            switch (propertyName.ToLowerInvariant())
            {
                case "id": return Ok(id);
                case "version": return Ok(version);
            }

            return BadRequest("Querying property " + propertyName + " is not supported.");
        }

        // GET/POST /Search()?searchTerm=&targetFramework=&includePrerelease=
        [HttpGet]
        [HttpPost]
        public virtual IHttpActionResult Search(
            ODataQueryOptions<ODataPackage> options,
            [FromODataUri] string searchTerm = "", 
            [FromODataUri] string targetFramework ="", 
            [FromODataUri] bool includePrerelease = false,
            [FromODataUri] bool includeDelisted=false)
        {
            var targetFrameworks = String.IsNullOrEmpty(targetFramework) ? Enumerable.Empty<string>() : targetFramework.Split('|');

            var sourceQuery = _serverRepository
                .Search(searchTerm, targetFrameworks, includePrerelease);

            return TransformToQueryResult(options, sourceQuery);
        }

        // GET /Search()/$count?searchTerm=&targetFramework=&includePrerelease=
        [HttpGet]
        public virtual IHttpActionResult SearchCount(
            ODataQueryOptions<ODataPackage> options,
            [FromODataUri]string searchTerm = "",
            [FromODataUri]string targetFramework = "",
            [FromODataUri]bool includePrerelease = false)
        {
            var searchResults = Search(options, searchTerm, targetFramework, includePrerelease);
            return searchResults.FormattedAsCountResult<ODataPackage>();
        }

        // GET/POST /GetUpdates()?packageIds=&versions=&includePrerelease=&includeAllVersions=&targetFrameworks=&versionConstraints=
        [HttpGet]
        [HttpPost]
        public virtual IHttpActionResult GetUpdates(
            ODataQueryOptions<ODataPackage> options,
            [FromODataUri]string packageIds,
            [FromODataUri]string versions,
            [FromODataUri]bool includePrerelease,
            [FromODataUri]bool includeAllVersions,
            [FromODataUri]string targetFrameworks = "",
            [FromODataUri]string versionConstraints = "")
        {
            if (string.IsNullOrEmpty(packageIds) || string.IsNullOrEmpty(versions))
            {
                return Ok(Enumerable.Empty<ODataPackage>().AsQueryable());
            }

            // Workaround https://github.com/NuGet/NuGetGallery/issues/674 for NuGet 2.1 client.
            // Can probably eventually be retired (when nobody uses 2.1 anymore...)
            // Note - it was URI un-escaping converting + to ' ', undoing that is actually a pretty conservative substitution because
            // space characters are never acepted as valid by VersionUtility.ParseFrameworkName.
            if (!string.IsNullOrEmpty(targetFrameworks))
            {
                targetFrameworks = targetFrameworks.Replace(' ', '+');
            }

            var idValues = packageIds.Trim().Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            var versionValues = versions.Trim().Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            var targetFrameworkValues = String.IsNullOrEmpty(targetFrameworks) 
                                        ? null 
                                        : targetFrameworks.Split('|').Select(VersionUtility.ParseFrameworkName).ToList();
            var versionConstraintValues = (String.IsNullOrEmpty(versionConstraints)
                                            ? new string[idValues.Length]
                                            : versionConstraints.Split('|')).ToList();

            if (idValues.Length == 0 || idValues.Length != versionValues.Length || idValues.Length != versionConstraintValues.Count)
            {
                // Exit early if the request looks invalid
                return Ok(Enumerable.Empty<ODataPackage>().AsQueryable());
            }

            var packagesToUpdate = new List<IPackageMetadata>();
            for (var i = 0; i < idValues.Length; i++)
            {
                SemanticVersion semVersion;
                if(SemanticVersion.TryParse(versionValues[i],out semVersion))
                {
                    packagesToUpdate.Add(new PackageBuilder { Id = idValues[i], Version = semVersion });
                }
                else
                {
                    versionConstraintValues.RemoveAt(i);
                }

            }

            var versionConstraintsList = new IVersionSpec[versionConstraintValues.Count];
            for (var i = 0; i < versionConstraintsList.Length; i++)
            {
                if (!String.IsNullOrEmpty(versionConstraintValues[i]))
                {
                    VersionUtility.TryParseVersionSpec(versionConstraintValues[i], out versionConstraintsList[i]);
                }
            }

            var sourceQuery = _serverRepository
                .GetUpdatesCore(packagesToUpdate, includePrerelease, includeAllVersions, targetFrameworkValues, versionConstraintsList);

            return TransformToQueryResult(options, sourceQuery);
        }

        // /api/v2/GetUpdates()/$count?packageIds=&versions=&includePrerelease=&includeAllVersions=&targetFrameworks=&versionConstraints=
        [HttpGet]
        [HttpPost]
        public virtual IHttpActionResult GetUpdatesCount(
            ODataQueryOptions<ODataPackage> options,
            [FromODataUri]string packageIds,
            [FromODataUri]string versions,
            [FromODataUri]bool includePrerelease,
            [FromODataUri]bool includeAllVersions,
            [FromODataUri]string targetFrameworks = "",
            [FromODataUri]string versionConstraints = "")
        {
            return GetUpdates(options, packageIds, versions, includePrerelease, includeAllVersions, targetFrameworks, versionConstraints)
                .FormattedAsCountResult<ODataPackage>();
        }

        /// <summary>
        /// Exposed as OData Action for specific entity
        /// GET/HEAD /Packages(Id=,Version=)/Download
        /// </summary>
        /// <param name="id"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        [HttpGet, HttpHead]
        public virtual HttpResponseMessage Download(string id, string version = "")
        {
            var requestedPackage = RetrieveFromRepository(id, version);

            if (requestedPackage == null)
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, string.Format("'Package {0} {1}' Not found.", id, version));

            var serverPackage = requestedPackage as ServerPackage;

            var responseMessage = Request.CreateResponse(HttpStatusCode.OK);

            if (Request.Method == HttpMethod.Get)
            {
                if (serverPackage != null)
                    responseMessage.Content = new StreamContent(File.OpenRead(serverPackage.FullPath));
                else
                    responseMessage.Content = new StreamContent(requestedPackage.GetStream());
            }
            else
            {
                responseMessage.Content = new StringContent(string.Empty);
            }

            responseMessage.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue("binary/octet-stream");
            if (serverPackage != null)
            {
                responseMessage.Content.Headers.LastModified = serverPackage.LastUpdated;
                responseMessage.Headers.ETag = new EntityTagHeaderValue('"' + serverPackage.PackageHash + '"');
            }

            responseMessage.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue(DispositionTypeNames.Attachment)
            {
                FileName = string.Format("{0}.{1}{2}", requestedPackage.Id, requestedPackage.Version, NuGet.Constants.PackageExtension),
                Size = serverPackage != null ? (long?)serverPackage.PackageSize : null,
                CreationDate = requestedPackage.Published,
                ModificationDate = responseMessage.Content.Headers.LastModified,
            };

            return responseMessage;
        }

        /// <summary>
        /// Exposed through ordinary Web API route. Bypasses OData pipeline.
        /// DELETE /id/version
        /// </summary>
        /// <param name="id"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        [HttpDelete]
        public virtual HttpResponseMessage DeletePackage(string id, string version)
        {
            if (_authenticationService == null)
                return Request.CreateErrorResponse(HttpStatusCode.Forbidden, "Package delete is not allowed");

            var apiKey = GetApiKeyFromHeader();

            var requestedPackage = RetrieveFromRepository(id, version);

            if (requestedPackage == null || !requestedPackage.Listed)
            {
                // Package not found
                return CreateStringResponse(HttpStatusCode.NotFound, string.Format("'Package {0} {1}' Not found.", id, version)); // Request.CreateErrorResponse(HttpStatusCode.NotFound, string.Format("'Package {0} {1}' Not found.", id, version));
            }

            // Make sure the user can access this package
            if (_authenticationService.IsAuthenticated(User, apiKey, requestedPackage.Id))
            {
                _serverRepository.RemovePackage(requestedPackage.Id, requestedPackage.Version);
                return Request.CreateResponse(HttpStatusCode.NoContent);
            }
            else
            {
                return CreateStringResponse(HttpStatusCode.Forbidden, string.Format("Access denied for package '{0}', version '{1}'.", requestedPackage.Id,version));
            }
        }

        /// <summary>
        /// Exposed through ordinary Web API route. Bypasses OData pipeline.
        /// PUT /
        /// </summary>
        /// <returns></returns>
        [HttpPut]
        public virtual async Task<HttpResponseMessage> UploadPackage()
        {
            if (_authenticationService == null)
                return Request.CreateErrorResponse(HttpStatusCode.Forbidden, "Package upload is not allowed");

            var apiKey = GetApiKeyFromHeader();

            // Copy the package to a temporary file
            var temporaryFile = Path.GetTempFileName();
            using (var temporaryFileStream = File.Open(temporaryFile, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                if (Request.Content.IsMimeMultipartContent())
                {
                    var multipartContents = await Request.Content.ReadAsMultipartAsync();
                    await multipartContents.Contents.First().CopyToAsync(temporaryFileStream);
                }
                else
                {
                    await Request.Content.CopyToAsync(temporaryFileStream);
                }
            }

            var package = new OptimizedZipPackage(temporaryFile);


            HttpResponseMessage retValue;
            if (_authenticationService.IsAuthenticated(User, apiKey, package.Id))
            {
                _serverRepository.AddPackage(package);
                retValue = Request.CreateResponse(HttpStatusCode.Created);
            }
            else
            {
                retValue = CreateStringResponse(HttpStatusCode.Forbidden, string.Format("Access denied for package '{0}'.", package.Id));
            }

            package = null;
            try
            {
                File.Delete(temporaryFile);
            }
            catch (Exception)
            {                
                retValue = CreateStringResponse(HttpStatusCode.InternalServerError, "Could not remove temporary upload file.");
            }

            return retValue;
        }

        protected HttpResponseMessage CreateStringResponse(HttpStatusCode statusCode, string response)
        {
            var responseMessage = new HttpResponseMessage(statusCode) { Content = new StringContent(response) };
            return responseMessage;
        }

        private string GetApiKeyFromHeader()
        {
            string apiKey = null;
            IEnumerable<string> values;
            if (Request.Headers.TryGetValues(ApiKeyHeader, out values))
                apiKey = values.FirstOrDefault();
            return apiKey;
        }

        protected IPackage RetrieveFromRepository(string id, string version)
        {
            return string.IsNullOrEmpty(version) ?
                                        _serverRepository.FindPackage(id) :
                                        _serverRepository.FindPackage(id, new SemanticVersion(version));
        }

        protected IQueryable<ODataPackage> TransformPackages(IEnumerable<IPackage> packages)
        {
            return packages
                .Distinct()
                .Select(x => x.AsODataPackage())
                .AsQueryable()
                .InterceptWith(new NormalizeVersionInterceptor());
        }

        /// <summary>
        /// Generates a QueryResult.
        /// </summary>
        /// <typeparam name="TModel">Model type.</typeparam>
        /// <param name="options">OData query options.</param>
        /// <param name="queryable">Queryable to build QueryResult from.</param>
        /// <param name="maxPageSize">Maximum page size.</param>
        /// <returns>A QueryResult instance.</returns>
        protected virtual IHttpActionResult QueryResult<TModel>(ODataQueryOptions<TModel> options, IQueryable<TModel> queryable, int maxPageSize)
        {
            return new QueryResult<TModel>(options, queryable, this, maxPageSize);
        }

        /// <summary>
        /// Transforms IPackages to ODataPackages and generates a QueryResult<ODataPackage></ODataPackage>
        /// </summary>
        /// <param name="options"></param>
        /// <param name="sourceQuery"></param>
        /// <returns></returns>
        protected virtual IHttpActionResult TransformToQueryResult(ODataQueryOptions<ODataPackage> options, IEnumerable<IPackage> sourceQuery)
        {
            var transformedQuery = TransformPackages(sourceQuery);
            return QueryResult(options, transformedQuery, _maxPageSize);
        }

    }
}
