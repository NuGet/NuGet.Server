using NuGet.Server.DataServices;
using NuGet.Server.Infrastructure;
using NuGet.Server.V2.OData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
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
        readonly IServerPackageRepository _repository;

        public NuGetODataController(IServerPackageRepository repository)
        {
            _repository = repository;
        }
        
        // /api/v2/Packages
        [HttpGet]
        [HttpPost]
        [EnableQuery(PageSize = 100, HandleNullPropagation = HandleNullPropagationOption.False)]
        public IQueryable<ODataPackage> Get()
        {
            var sourceQuery = _repository.GetPackages();
            return TransformPackages(sourceQuery);
        }

        // /api/v2/Packages(Id=,Version=)
        [HttpGet]
        public ODataPackage Get([FromODataUri] string id, [FromODataUri] string version)
        {
            var semVersion = new SemanticVersion(version);
            var package = _repository.FindPackage(id, semVersion);
            if (package == null)
                throw new HttpResponseException(HttpStatusCode.NotFound);

            return package.AsODataPackage();
        }

        // /api/v2/FindPackagesById()?id=
        [HttpGet]
        [HttpPost]
        [EnableQuery(PageSize = 100, HandleNullPropagation = HandleNullPropagationOption.False)]
        public IEnumerable<ODataPackage> FindPackagesById([FromODataUri] string id)
        {
            var sourceQuery = _repository.FindPackagesById(id);
            return TransformPackages(sourceQuery);
        }


        // /api/v2/Search()?searchTerm=&targetFramework=&includePrerelease=
        [HttpGet]
        [HttpPost]
        [EnableQuery(PageSize = 100, HandleNullPropagation = HandleNullPropagationOption.False)]
        public IQueryable<ODataPackage> Search(
            [FromODataUri] string searchTerm = "", 
            [FromODataUri] string targetFramework ="", 
            [FromODataUri] bool includePrerelease = false)
        {
            var targetFrameworks = String.IsNullOrEmpty(targetFramework) ? Enumerable.Empty<string>() : targetFramework.Split('|');

            var sourceQuery = _repository
                .Search(searchTerm, targetFrameworks, includePrerelease);

            return TransformPackages(sourceQuery);
        }

        // /api/v2/GetUpdates()?packageIds=&versions=&includePrerelease=&includeAllVersions=&targetFrameworks=&versionConstraints=
        [HttpGet]
        [HttpPost]
        [EnableQuery(PageSize = 100, HandleNullPropagation = HandleNullPropagationOption.False)]
        public IQueryable<ODataPackage> GetUpdates(
            [FromODataUri] string packageIds,
            [FromODataUri] string versions,
            [FromODataUri] bool includePrerelease,
            [FromODataUri] bool includeAllVersions,
            [FromODataUri] string targetFrameworks = "",
            [FromODataUri] string versionConstraints = "")
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

        IQueryable<ODataPackage> TransformPackages(IEnumerable<IPackage> packages)
        {
            var retValue = packages.Select(x => x.AsODataPackage())
                .AsQueryable()
                .InterceptWith(new NormalizeVersionInterceptor());
            return retValue;
        }

    }
}
