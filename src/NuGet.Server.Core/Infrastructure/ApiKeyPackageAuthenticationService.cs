using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace NuGet.Server.Core.Infrastructure
{
    /// <summary>
    /// Default implementation of IPackageAuthenticationService.
    /// Ignores principial and packageId passed inn to IsAuthenticated() and simply checks the apiKey.
    /// If requireApiKey=false, no check is done and all calls are authenticated.
    /// </summary>
    public class ApiKeyPackageAuthenticationService: IPackageAuthenticationService
    {
        readonly bool _requireApiKey;
        readonly string _apiKey;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="requireApiKey"></param>
        /// <param name="apiKey">Must be supplied if requireApiKey is true. Should be null when requireApiKey is false.</param>
        public ApiKeyPackageAuthenticationService(bool requireApiKey, string apiKey)
        {
            _requireApiKey = requireApiKey;
            _apiKey = apiKey;
        }

        public bool IsAuthenticated(IPrincipal user, string apiKey, string packageId)
        {
            return IsAuthenticatedInternal(apiKey);
        }

        internal bool IsAuthenticatedInternal(string apiKey)
        {
            if (_requireApiKey == false)
            {
                // If the server's configured to allow pushing without an ApiKey, all requests are assumed to be authenticated.
                return true;
            }

            // No api key, no-one can push
            if (String.IsNullOrEmpty(_apiKey))
            {
                return false;
            }

            return string.Equals(apiKey, _apiKey, StringComparison.OrdinalIgnoreCase);
        }
    }
}
