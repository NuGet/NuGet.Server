using NuGet.Server.DataServices;

namespace NuGet.Server.Infrastructure
{
    public interface IServerPackageRepository : IServiceBasedRepository
    {
        void RemovePackage(string packageId, SemanticVersion version);
        Package GetMetadataPackage(IPackage package);
    }
}
