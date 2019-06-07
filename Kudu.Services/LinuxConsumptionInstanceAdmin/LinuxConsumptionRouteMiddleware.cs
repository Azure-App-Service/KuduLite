using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Kudu.Services.LinuxConsumptionInstanceAdmin
{
    /// <summary>
    /// Middleware to filter out unnecessary routes when running in Linux Consumption
    /// </summary>
    public class LinuxConsumptionRouteMiddleware
    {
        /// <summary>
        /// A list of route prefixes which allows to be accessed
        /// </summary>
        public static readonly string[] Whitelist = new string[]
        {
            "/api/zipdeploy",
            "/admin/instance",
            "/deployments",
        };

        private readonly RequestDelegate _next;
        private readonly PathString[] _whitelistedPathString;

        /// <summary>
        /// Filter out unnecessary routes for Linux Consumption
        /// </summary>
        /// <param name="next">The next request middleware to be passed in</param>
        public LinuxConsumptionRouteMiddleware(RequestDelegate next)
        {
            _next = next;
            _whitelistedPathString = new PathString[Whitelist.Length];
            for (int i = 0; i < _whitelistedPathString.Length; i++)
            {
                _whitelistedPathString[i] = new PathString(Whitelist[i]);
            }
        }

        /// <summary>
        /// Detect if a route matches any of whitelisted prefixes
        /// </summary>
        /// <param name="context">Http request context</param>
        /// <returns>Response be set to 404 if the route is not whitelisted</returns>
        public async Task Invoke(HttpContext context)
        {
            if (!IsRouteWhitelisted(context.Request.Path))
            {
                context.Response.StatusCode = 404;
                return;
            }

            await _next.Invoke(context);
        }

        private bool IsRouteWhitelisted(PathString routePath)
        {
            for (int i = 0; i < _whitelistedPathString.Length; i++)
            {
                if (routePath.StartsWithSegments(_whitelistedPathString[i]))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
