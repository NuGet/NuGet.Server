using System.Web;

namespace NuGet.Server
{
    public interface IPackageService
    {
        void CreatePackage(HttpContextBase context);

        void PublishPackage(HttpContextBase context);

        void DeletePackage(HttpContextBase context);

        void DownloadPackage(HttpContextBase context);
    }
}