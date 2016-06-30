using NuGet.Server.Core.DataServices;
using NuGet.Server.Core.Infrastructure;
using NuGet.Server.V2.OData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.OData;
using System.Web.Http.OData.Query;

namespace NuGet.Server.V2.Controllers
{
    [NuGetODataControllerConfiguration]
    public abstract class NuGetODataController : ODataController
    {
        const string ApiKeyHeader = "X-NUGET-APIKEY";

        protected readonly IServerPackageRepository _repository;
        protected readonly IPackageAuthenticationService _authenticationService;

        public NuGetODataController(IServerPackageRepository repository, IPackageAuthenticationService authenticationService=null)
        {
            _repository = repository;
            _authenticationService = authenticationService;
        }
        
        // GET /Packages
        // Never seen this invoked. NuGet.Exe and Visual Studio seems to use 'Search' for all package listing.
        [HttpGet]
        [HttpPost]
        [EnableQuery(PageSize = 100, HandleNullPropagation = HandleNullPropagationOption.False)]
        public virtual IQueryable<ODataPackage> Get()
        {
            var sourceQuery = _repository.GetPackages();
            return TransformPackages(sourceQuery);
        }

        // GET /Packages(Id=,Version=)
        [HttpGet]
        public virtual ODataPackage Get(string id, string version)
        {
            var package = RetrieveFromRepository(id, version);

            if (package == null)
                throw new HttpResponseException(HttpStatusCode.NotFound);

            return package.AsODataPackage();
        }


        // GET/POST /FindPackagesById()?id=
        [HttpGet]
        [HttpPost]
        [EnableQuery(PageSize = 100, HandleNullPropagation = HandleNullPropagationOption.False)]
        public virtual IQueryable<ODataPackage> FindPackagesById([FromODataUri] string id)
        {
            var sourceQuery = _repository.FindPackagesById(id);
            return TransformPackages(sourceQuery);
        }


        // GET/POST /Search()?searchTerm=&targetFramework=&includePrerelease=
        [HttpGet]
        [HttpPost]
        [EnableQuery(PageSize = 100, HandleNullPropagation = HandleNullPropagationOption.False)]
        public virtual IQueryable<ODataPackage> Search(
            [FromODataUri] string searchTerm = "", 
            [FromODataUri] string targetFramework ="", 
            [FromODataUri] bool includePrerelease = false,
            [FromODataUri] bool includeDelisted=false)
        {
            var targetFrameworks = String.IsNullOrEmpty(targetFramework) ? Enumerable.Empty<string>() : targetFramework.Split('|');

            var sourceQuery = _repository
                .Search(searchTerm, targetFrameworks, includePrerelease);

            return TransformPackages(sourceQuery);
        }

        // GET/POST /GetUpdates()?packageIds=&versions=&includePrerelease=&includeAllVersions=&targetFrameworks=&versionConstraints=
        // Never seen this invoked. Visual Studio and NuGet.exe both seems to use 'FindPackagesById' for updates.
        [HttpGet]
        [HttpPost]
        [EnableQuery(PageSize = 100, HandleNullPropagation = HandleNullPropagationOption.False)]
        public virtual IQueryable<ODataPackage> GetUpdates(
            [FromODataUri] string packageIds,
            [FromODataUri] string versions,
            [FromODataUri] bool includePrerelease,
            [FromODataUri] bool includeAllVersions,
            [FromODataUri] string targetFrameworks,
            [FromODataUri] string versionConstraints)
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

            var sourceQuery = _repository
                .GetUpdatesCore(packagesToUpdate, includePrerelease, includeAllVersions, targetFrameworkValues, versionConstraintsList);

            return TransformPackages(sourceQuery);
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
            IPackage requestedPackage = RetrieveFromRepository(id, version);

            if (requestedPackage == null)
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, string.Format("'Package {0} {1}' Not found.", id, version));

            var serverPackage = requestedPackage as ServerPackage;

            var result = Request.CreateResponse(HttpStatusCode.OK);

            if (Request.Method == HttpMethod.Get)
            {
                if (serverPackage != null)
                    result.Content = new StreamContent(File.OpenRead(serverPackage.FullPath));
                else
                    result.Content = new StreamContent(requestedPackage.GetStream());
            }
            else
            {
                result.Content = new StringContent(string.Empty);
            }

            result.Content.Headers.ContentType = new MediaTypeWithQualityHeaderValue("binary/octet-stream");
            if (serverPackage != null)
            {
                result.Content.Headers.LastModified = serverPackage.LastUpdated;
                result.Headers.ETag = new EntityTagHeaderValue('"' + serverPackage.PackageHash + '"');
            }

            result.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue(DispositionTypeNames.Attachment)
            {
                FileName = string.Format("{0}.{1}{2}", requestedPackage.Id, requestedPackage.Version, NuGet.Constants.PackageExtension),
                Size = serverPackage != null ? (long?)serverPackage.PackageSize : null,
                CreationDate = requestedPackage.Published,
                ModificationDate = result.Content.Headers.LastModified,
            };

            return result;
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

            return Request.CreateResponse(HttpStatusCode.Accepted);
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

            // Get the api key from the header
            string apiKey = null;
            IEnumerable<string> values;
            if (Request.Headers.TryGetValues(ApiKeyHeader, out values))
                apiKey = values.FirstOrDefault();


            // Copy the package to a temporary file
            var temporaryFile = Path.GetTempFileName();
            using (var temporaryFileStream = File.Open(temporaryFile, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                if (Request.Content.IsMimeMultipartContent())
                {
                    var provider = new MultipartMemoryStreamProvider();
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
            // Make sure the user can access this package
            if (_authenticationService.IsAuthenticated(User, apiKey, package.Id))
            {
                _repository.AddPackage(package);
                retValue = Request.CreateResponse(HttpStatusCode.Created);
            }
            else
            {
                retValue = Request.CreateErrorResponse(HttpStatusCode.Forbidden, string.Format("Access denied for package '{0}'.", package.Id));
            }

            package = null;
            try
            {
                File.Delete(temporaryFile);
            }
            catch (Exception)
            {
                retValue = Request.CreateErrorResponse(HttpStatusCode.InternalServerError, "Could not remove temporary upload file.");
            }

            return retValue;
        }

        protected IPackage RetrieveFromRepository(string id, string version)
        {
            return string.IsNullOrEmpty(version) ?
                                        _repository.FindPackage(id) :
                                        _repository.FindPackage(id, new SemanticVersion(version));
        }

        protected IQueryable<ODataPackage> TransformPackages(IEnumerable<IPackage> packages)
        {
            var retValue = packages.Select(x => x.AsODataPackage())
                .AsQueryable()
                .InterceptWith(new NormalizeVersionInterceptor());
            return retValue;
        }

    }
}
