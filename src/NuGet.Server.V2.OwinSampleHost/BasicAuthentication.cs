using Microsoft.Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;


namespace NuGet.Server.V2.OwinSampleHost
{
    /// <summary>
    /// Authenticates all users that supply a non-empty username and password.
    /// All usernames that starts with 'admin' are assigned to the 'Admin' role.
    /// For demonstratiion purposes only!
    /// 
    /// Modify this class or 
    ///   - override ValidateUser method to provide a real username/password check.
    ///   - override GetRolesForUser method to return roles based on usernames.
    /// </summary>
    public class BasicAuthentication : OwinMiddleware
    {
        public BasicAuthentication(OwinMiddleware next) :
            base(next)
        {

        }

        public override async Task Invoke(IOwinContext context)
        {
            var response = context.Response;
            var request = context.Request;

            response.OnSendingHeaders(state =>
            {
                var owinResponse = (OwinResponse)state;

                if (owinResponse.StatusCode == 401)
                {
                    owinResponse.Headers.Add("WWW-Authenticate", new[] { "Basic" });
                }
            }, response);

            var header = request.Headers["Authorization"];

            if (!String.IsNullOrWhiteSpace(header))
            {
                var authHeader = System.Net.Http.Headers.AuthenticationHeaderValue.Parse(header);

                if ("Basic".Equals(authHeader.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    var parameter = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader.Parameter));

                    var parts = parameter.Split(':');

                    var username = parts[0];
                    var password = parts[1];

                    if(ValidateUser(username, password))
                    {
                        SetClaimsIdentity(request, username);
                    }
                }
            }

            await Next.Invoke(context);
        }

        protected virtual void SetClaimsIdentity(IOwinRequest request, string username)
        {
            var claims = new [] 
            {
                new Claim(ClaimTypes.Name, username)
            }
            .Concat(
                GetRolesForUser(username).Select(r=>new Claim(ClaimTypes.Role, r))
                );

            var id = new ClaimsIdentity(claims, "Basic");
            request.User = new ClaimsPrincipal(id);
        }

        protected virtual IEnumerable<string> GetRolesForUser(string username)
        {
            if (username.ToLower().StartsWith("admin"))
                return new[] { "Admin" };

            return Enumerable.Empty<string>();
        }

        protected virtual bool ValidateUser(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                return false;
            }

            return true;
        }
    }
}
