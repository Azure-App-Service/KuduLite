using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Kudu.Services.Infrastructure.Authorization;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication;
using Kudu.Services.Infrastructure.Authentication;

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
        private const string DisguisedHostHeader = "DISGUISED-HOST";
        private const string HostHeader = "HOST";
        private const string ForwardedProtocolHeader = "X-Forwarded-Proto";
        private const string AuthorizationPolicy = AuthPolicyNames.LinuxConsumptionRestriction;

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
        /// <param name="authorizationService">Authorization service for each request</param>
        /// <returns>Response be set to 404 if the route is not whitelisted</returns>
        public async Task Invoke(HttpContext context, IAuthorizationService authorizationService = null)
        {
            // Step 1: if disguise host exists, replace the request header HOST to disguise header
            if (context.Request.Headers.TryGetValue(DisguisedHostHeader, out StringValues value))
            {
                context.Request.Headers[HostHeader] = value;
            }

            if (context.Request.Headers.TryGetValue(ForwardedProtocolHeader, out value))
            {
                context.Request.Scheme = value;
            }

            // Step 2: check if the request endpoint is enabled in Linux Consumption
            if (!IsRouteWhitelisted(context.Request.Path))
            {
                context.Response.StatusCode = 404;
                return;
            }

            // Step 3: check if the request matches authorization policy
            AuthenticateResult authenticateResult = await context.AuthenticateAsync(ArmAuthenticationDefaults.AuthenticationScheme);
            if (!authenticateResult.Succeeded)
            {
                context.Response.StatusCode = 401;
                return;
            }

            if (authorizationService != null)
            {
                AuthorizationResult authorizeResult = await authorizationService.AuthorizeAsync(authenticateResult.Principal, AuthorizationPolicy);
                if (!authorizeResult.Succeeded)
                {
                    context.Response.StatusCode = 401;
                    return;
                }
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
