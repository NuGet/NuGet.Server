using System.Linq;
using NuGet.Server.Infrastructure;

namespace NuGet.Server.DataServices
{
    public class PackageContext
    {
        private readonly IServerPackageRepository _repository;
        public PackageContext(IServerPackageRepository repository)
        {
            _repository = repository;
        }

        public IQueryable<Package> Packages
        {
            get
            {
                return _repository.GetPackages()
                            .Select(_repository.GetMetadataPackage)
                            .Where(p => p != null)
                            .AsQueryable()
                            .InterceptWith(new PackageIdComparisonVisitor());
            }
        }
    }
}
