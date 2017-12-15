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
using System.Threading;
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
        protected NuGetODataController(
            IServerPackageRepository repository,
            IPackageAuthenticationService authenticationService = null)
        {
            _serverRepository = repository ?? throw new ArgumentNullException(nameof(repository));
            _authenticationService = authenticationService;
        }
        
        // GET /Packages
        [HttpGet]
        public virtual async Task<IHttpActionResult> Get(
            ODataQueryOptions<ODataPackage> options,
            [FromUri] string semVerLevel = "",
            CancellationToken token = default(CancellationToken))
        {
            var clientCompatibility = ClientCompatibilityFactory.FromProperties(semVerLevel);

            var sourceQuery = await _serverRepository.GetPackagesAsync(clientCompatibility, token);

            return TransformToQueryResult(options, sourceQuery, clientCompatibility);
        }

        // GET /Packages/$count
        [HttpGet]
        public virtual async Task<IHttpActionResult> GetCount(
            ODataQueryOptions<ODataPackage> options,
            [FromUri] string semVerLevel = "",
            CancellationToken token = default(CancellationToken))
        {
            return (await Get(options, semVerLevel, token)).FormattedAsCountResult<ODataPackage>();
        }

        // GET /Packages(Id=,Version=)
        [HttpGet]
        public virtual async Task<IHttpActionResult> Get(
            ODataQueryOptions<ODataPackage> options,
            string id,
            string version,
            CancellationToken token)
        {
            var package = await RetrieveFromRepositoryAsync(id, version, token);

            if (package == null)
            {
                return NotFound();
            }

            return TransformToQueryResult(options, new[] { package }, ClientCompatibility.Max)
                .FormattedAsSingleResult<ODataPackage>();
        }

        // GET/POST /FindPackagesById()?id=
        [HttpGet]
        [HttpPost]
        public virtual async Task<IHttpActionResult> FindPackagesById(
            ODataQueryOptions<ODataPackage> options,
            [FromODataUri] string id,
            [FromUri] string semVerLevel = "",
            CancellationToken token = default(CancellationToken))
        {
            if (string.IsNullOrEmpty(id))
            {
                var emptyResult = Enumerable.Empty<ODataPackage>().AsQueryable();
                return QueryResult(options, emptyResult, _maxPageSize);
            }

            var clientCompatibility = ClientCompatibilityFactory.FromProperties(semVerLevel);

            var sourceQuery = await _serverRepository.FindPackagesByIdAsync(id, clientCompatibility, token);

            return TransformToQueryResult(options, sourceQuery, clientCompatibility);
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
        public virtual async Task<IHttpActionResult> Search(
            ODataQueryOptions<ODataPackage> options,
            [FromODataUri] string searchTerm = "", 
            [FromODataUri] string targetFramework = "", 
            [FromODataUri] bool includePrerelease = false,
            [FromODataUri] bool includeDelisted = false,
            [FromUri] string semVerLevel = "",
            CancellationToken token = default(CancellationToken))
        {
            var targetFrameworks = String.IsNullOrEmpty(targetFramework) ? Enumerable.Empty<string>() : targetFramework.Split('|');

            var clientCompatibility = ClientCompatibilityFactory.FromProperties(semVerLevel);

            var sourceQuery = await _serverRepository.SearchAsync(
                searchTerm,
                targetFrameworks,
                includePrerelease,
                clientCompatibility,
                token);

            return TransformToQueryResult(options, sourceQuery, clientCompatibility);
        }

        // GET /Search()/$count?searchTerm=&targetFramework=&includePrerelease=
        [HttpGet]
        public virtual async Task<IHttpActionResult> SearchCount(
            ODataQueryOptions<ODataPackage> options,
            [FromODataUri] string searchTerm = "",
            [FromODataUri] string targetFramework = "",
            [FromODataUri] bool includePrerelease = false,
            [FromODataUri] bool includeDelisted = false,
            [FromUri] string semVerLevel = "",
            CancellationToken token = default(CancellationToken))
        {
            var searchResults = await Search(
                options,
                searchTerm,
                targetFramework,
                includePrerelease,
                includeDelisted,
                semVerLevel,
                token);

            return searchResults.FormattedAsCountResult<ODataPackage>();
        }

        // GET/POST /GetUpdates()?packageIds=&versions=&includePrerelease=&includeAllVersions=&targetFrameworks=&versionConstraints=
        [HttpGet]
        [HttpPost]
        public virtual async Task<IHttpActionResult> GetUpdates(
            ODataQueryOptions<ODataPackage> options,
            [FromODataUri] string packageIds,
            [FromODataUri] string versions,
            [FromODataUri] bool includePrerelease,
            [FromODataUri] bool includeAllVersions,
            [FromODataUri] string targetFrameworks = "",
            [FromODataUri] string versionConstraints = "",
            [FromUri] string semVerLevel = "",
            CancellationToken token = default(CancellationToken))
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
                if(SemanticVersion.TryParse(versionValues[i], out var semVersion))
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

            var clientCompatibility = ClientCompatibilityFactory.FromProperties(semVerLevel);

            var sourceQuery = await _serverRepository.GetUpdatesAsync(
                packagesToUpdate,
                includePrerelease,
                includeAllVersions,
                targetFrameworkValues,
                versionConstraintsList,
                clientCompatibility,
                token);

            return TransformToQueryResult(options, sourceQuery, clientCompatibility);
        }

        // /api/v2/GetUpdates()/$count?packageIds=&versions=&includePrerelease=&includeAllVersions=&targetFrameworks=&versionConstraints=
        [HttpGet]
        [HttpPost]
        public virtual async Task<IHttpActionResult> GetUpdatesCount(
            ODataQueryOptions<ODataPackage> options,
            [FromODataUri] string packageIds,
            [FromODataUri] string versions,
            [FromODataUri] bool includePrerelease,
            [FromODataUri] bool includeAllVersions,
            [FromODataUri] string targetFrameworks = "",
            [FromODataUri] string versionConstraints = "",
            [FromUri] string semVerLevel = "",
            CancellationToken token = default(CancellationToken))
        {
            var updates = await GetUpdates(
                options,
                packageIds,
                versions,
                includePrerelease,
                includeAllVersions,
                targetFrameworks,
                versionConstraints,
                semVerLevel,
                token);

            return updates.FormattedAsCountResult<ODataPackage>();
        }

        /// <summary>
        /// Exposed as OData Action for specific entity
        /// GET/HEAD /Packages(Id=,Version=)/Download
        /// </summary>
        /// <param name="id"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        [HttpGet, HttpHead]
        public virtual async Task<HttpResponseMessage> Download(
            string id,
            string version = "",
            CancellationToken token = default(CancellationToken))
        {
            var requestedPackage = await RetrieveFromRepositoryAsync(id, version, token);

            if (requestedPackage == null)
            {
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, string.Format("'Package {0} {1}' Not found.", id, version));
            }

            var responseMessage = Request.CreateResponse(HttpStatusCode.OK);

            if (Request.Method == HttpMethod.Get)
            {
                responseMessage.Content = new StreamContent(File.OpenRead(requestedPackage.FullPath));
            }
            else
            {
                responseMessage.Content = new StringContent(string.Empty);
            }

            responseMessage.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue("binary/octet-stream");
            if (requestedPackage != null)
            {
                responseMessage.Content.Headers.LastModified = requestedPackage.LastUpdated;
                responseMessage.Headers.ETag = new EntityTagHeaderValue('"' + requestedPackage.PackageHash + '"');
            }

            responseMessage.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue(DispositionTypeNames.Attachment)
            {
                FileName = string.Format("{0}.{1}{2}", requestedPackage.Id, requestedPackage.Version, NuGet.Constants.PackageExtension),
                Size = requestedPackage != null ? (long?)requestedPackage.PackageSize : null,
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
        public virtual async Task<HttpResponseMessage> DeletePackage(
            string id,
            string version,
            CancellationToken token)
        {
            if (_authenticationService == null)
            {
                return Request.CreateErrorResponse(HttpStatusCode.Forbidden, "Package delete is not allowed");
            }

            var apiKey = GetApiKeyFromHeader();

            var requestedPackage = await RetrieveFromRepositoryAsync(id, version, token);

            if (requestedPackage == null || !requestedPackage.Listed)
            {
                // Package not found
                return CreateStringResponse(HttpStatusCode.NotFound, string.Format("'Package {0} {1}' Not found.", id, version)); // Request.CreateErrorResponse(HttpStatusCode.NotFound, string.Format("'Package {0} {1}' Not found.", id, version));
            }

            // Make sure the user can access this package
            if (_authenticationService.IsAuthenticated(User, apiKey, requestedPackage.Id))
            {
                await _serverRepository.RemovePackageAsync(requestedPackage.Id, requestedPackage.Version, token);
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
        public virtual async Task<HttpResponseMessage> UploadPackage(CancellationToken token)
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
                await _serverRepository.AddPackageAsync(package, token);
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
            if (Request.Headers.TryGetValues(ApiKeyHeader, out var values))
            {
                apiKey = values.FirstOrDefault();
            }
                
            return apiKey;
        }

        protected async Task<IServerPackage> RetrieveFromRepositoryAsync(
            string id,
            string version,
            CancellationToken token)
        {
            if (string.IsNullOrEmpty(version))
            {
                return await _serverRepository.FindPackageAsync(id, ClientCompatibility.Max, token);
            }

            return await _serverRepository.FindPackageAsync(id, new SemanticVersion(version), token);
        }

        protected IQueryable<ODataPackage> TransformPackages(
            IEnumerable<IServerPackage> packages,
            ClientCompatibility compatibility)
        {
            return packages
                .Distinct()
                .Select(x => x.AsODataPackage(compatibility))
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
        protected virtual IHttpActionResult TransformToQueryResult(
            ODataQueryOptions<ODataPackage> options,
            IEnumerable<IServerPackage> sourceQuery,
            ClientCompatibility compatibility)
        {
            var transformedQuery = TransformPackages(sourceQuery, compatibility);
            return QueryResult(options, transformedQuery, _maxPageSize);
        }
    }
}
