using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Kudu.Services.Infrastructure.Authorization;
using Kudu.Services.Infrastructure.Authentication;
using System;
using System.Text.RegularExpressions;
using Kudu.Core.Tracing;
using Kudu.Core.Infrastructure;
using Kudu.Contracts.Settings;
using Kudu.Services.Infrastructure;
using Microsoft.AspNetCore.Http.Extensions;
using System.Text;

namespace Kudu.Services.LinuxConsumptionInstanceAdmin
{
    /// <summary>
    /// Middleware to filter out unnecessary routes when running in Linux Consumption
    /// </summary>
    public class LinuxConsumptionRouteMiddleware
    {
        private static readonly HashSet<string> Whitelist = new HashSet<string>
        {
            // Home Page Resources
            "/favicon.ico",
            "/Content/Images",

            // API Endpoints
            "/api/zipdeploy",
            "/api/deployments",
            "/api/isdeploying",
            "/api/settings",
            "/admin/instance",
            "/deployments",
            "/zipdeploy"
        };

        private readonly RequestDelegate _next;
        private readonly HashSet<PathString> _allowedPaths;
        private const string DisguisedHostHeader = "DISGUISED-HOST";
        private const string HostHeader = "HOST";
        private const string ForwardedProtocolHeader = "X-Forwarded-Proto";
        private const string AuthorizationPolicy = AuthPolicyNames.LinuxConsumptionRestriction;

        private static Regex malformedScmHostnameRegex = new Regex(@"^~\d+");
        private static string HomePageRoute = "/";

        /// <summary>
        /// Filter out unnecessary routes for Linux Consumption
        /// </summary>
        /// <param name="next">The next request middleware to be passed in</param>
        public LinuxConsumptionRouteMiddleware(RequestDelegate next)
        {
            _next = next;
            _allowedPaths = new HashSet<PathString>(Whitelist.Count);
            foreach (string pathString in Whitelist)
            {
                _allowedPaths.Add(new PathString(pathString));
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
            DateTime requestTime = DateTime.UtcNow;

            // Step 1: if disguise host exists, replace the request header HOST to DISGUISED-HOST
            //         if disguist host does not exist, check and replace ~1 with regex
            if (context.Request.Headers.TryGetValue(DisguisedHostHeader, out StringValues value))
            {
                context.Request.Headers[HostHeader] = value;
            }
            else
            {
                context.Request.Host = new HostString(SanitizeScmUrl(
                    context.Request.Headers[HostHeader].FirstOrDefault()));
            }

            if (context.Request.Headers.TryGetValue(ForwardedProtocolHeader, out value))
            {
                context.Request.Scheme = value;
            }

            // Step 2: check if the request endpoint is enabled in Linux Consumption
            if (!IsRouteAllowed(context.Request.Path))
            {
                context.Response.StatusCode = 404;
                KuduEventGenerator.Log().ApiEvent(
                    ServerConfiguration.GetApplicationName(),
                    "BlacklistedLinuxConsumptionEndpoint",
                    context.Request.GetEncodedPathAndQuery(),
                    context.Request.Method,
                    System.Environment.GetEnvironmentVariable("x-ms-request-id") ?? string.Empty,
                    context.Response.StatusCode,
                    (DateTime.UtcNow - requestTime).Milliseconds,
                    context.Request.GetUserAgent());
                return;
            }

            // Step 3: check if the request matches authorization policy
            // If the home page is requested without authentication (e.g. ControllerPing), return 200 with hint.
            // If the home page is requested with authentication (e.g. Customer Browser Access), return 200 with homepage content.
            AuthenticateResult authenticationResult = await context.AuthenticateAsync(ArmAuthenticationDefaults.AuthenticationScheme);
            if (IsHomePageWithoutAuthentication(authenticationResult, context.Request.Path))
            {
                byte[] data = Encoding.UTF8.GetBytes("Please use /basicAuth endpoint or AAD to authenticate SCM site");
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/plain; charset=UTF-8";
                await context.Response.Body.WriteAsync(data, 0, data.Length);
                KuduEventGenerator.Log().ApiEvent(
                    ServerConfiguration.GetApplicationName(),
                    "AccessLinuxConsumptionHomePageWithoutAuthentication",
                    context.Request.GetEncodedPathAndQuery(),
                    context.Request.Method,
                    System.Environment.GetEnvironmentVariable("x-ms-request-id") ?? string.Empty,
                    context.Response.StatusCode,
                    (DateTime.UtcNow - requestTime).Milliseconds,
                    context.Request.GetUserAgent());
                return;
            }
            else if (!authenticationResult.Succeeded)
            {
                context.Response.StatusCode = 401;
                KuduEventGenerator.Log().ApiEvent(
                    ServerConfiguration.GetApplicationName(),
                    "UnauthenticatedLinuxConsumptionEndpoint",
                    context.Request.GetEncodedPathAndQuery(),
                    context.Request.Method,
                    System.Environment.GetEnvironmentVariable("x-ms-request-id") ?? string.Empty,
                    context.Response.StatusCode,
                    (DateTime.UtcNow - requestTime).Milliseconds,
                    context.Request.GetUserAgent());
                return;
            }

            if (authorizationService != null)
            {
                AuthorizationResult endpointAuthorization = await authorizationService.AuthorizeAsync(authenticationResult.Principal, AuthorizationPolicy);
                if (!endpointAuthorization.Succeeded)
                {
                    context.Response.StatusCode = 401;
                    KuduEventGenerator.Log().ApiEvent(
                        ServerConfiguration.GetApplicationName(),
                        "UnauthorizedLinuxConsumptionEndpoint",
                        context.Request.GetEncodedPathAndQuery(),
                        context.Request.Method,
                        System.Environment.GetEnvironmentVariable("x-ms-request-id") ?? string.Empty,
                        context.Response.StatusCode,
                        (DateTime.UtcNow - requestTime).Milliseconds,
                        context.Request.GetUserAgent());
                    return;
                }
            }
            await _next.Invoke(context);
        }

        private bool IsRouteAllowed(PathString routePath)
        {
            if (IsHomePageRoute(routePath)) {
                return true;
            }
            return _allowedPaths.Any((ps) => routePath.StartsWithSegments(ps));
        }

        private bool IsHomePageRoute(PathString routePath)
        {
            return routePath.ToString() == HomePageRoute;
        }

        private bool IsHomePageWithoutAuthentication(AuthenticateResult authenticationResult, PathString routePath)
        {
            return !authenticationResult.Succeeded && IsHomePageRoute(routePath);
        }

        private static string SanitizeScmUrl(string malformedUrl)
        {
            if (string.IsNullOrEmpty(malformedUrl))
            {
                return malformedUrl;
            }

            return malformedScmHostnameRegex.Replace(malformedUrl, string.Empty, 1);
        }
    }

    /// <summary>
    /// Extension wrapper for using Linux Consumption Route Middleware
    /// </summary>
    public static class LinuxConsumptionRouteMiddlewareExtension
    {
        public static IApplicationBuilder UseLinuxConsumptionRouteMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<LinuxConsumptionRouteMiddleware>();
        }
    }
}
