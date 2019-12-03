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
using Kudu.Core;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using Kudu.Core.Helpers;

namespace Kudu.Services.Web
{
    /// <summary>
    /// Middleware to modify Kudu Context when running on an K8 Cluster
    /// </summary>
    public class KubeMiddleware
    {

        private readonly RequestDelegate _next;

        /// <summary>
        /// Filter out unnecessary routes for Linux Consumption
        /// </summary>
        /// <param name="next">The next request middleware to be passed in</param>
        public KubeMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        /// <summary>
        /// Detect if a route matches any of whitelisted prefixes
        /// </summary>
        /// <param name="context">Http request context</param>
        /// <param name="authorizationService">Authorization service for each request</param>
        /// <returns>Response be set to 404 if the route is not whitelisted</returns>
        public async Task Invoke(HttpContext context, IEnvironment environment, IServerConfiguration serverConfig)
        {
            var result = await context.AuthenticateAsync();
            if (result.Failure != null)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync(result.Failure.ToString(), Encoding.UTF8);
            }
            

            var host = context.Request.Headers["HTTP_HOST"].ToString();
            var appName = host.Substring(0, host.IndexOf("."));

            Console.WriteLine("APP : " + appName);

            if (IsGitRoute(context.Request.Path))
            {
                string[] pathParts = context.Request.Path.ToString().Split("/");

                string homeDir = "";
                string siteRepoDir = "";
                if (OSDetector.IsOnWindows())
                {
                    homeDir = "F:\\repos\\apps\\";
                    siteRepoDir = "\\site\\repository";
                }
                else
                {
                    homeDir = "/home/apps/";
                    siteRepoDir = "/site/repository";
                }

                if (pathParts != null && pathParts.Length >= 1)
                {
                    var appName = pathParts[1];
                    appName = appName.Trim().Replace(".git", "");
                    if(!FileSystemHelpers.DirectoryExists(homeDir + appName))
                    {
                        context.Response.StatusCode = 404;
                        await context.Response.WriteAsync("The repository does not exist", Encoding.UTF8);
                        return;
                    }
                    else
                    {
                        serverConfig.GitServerRoot = appName + ".git";
                        environment.RepositoryPath = homeDir + appName + siteRepoDir;
                    }
                }
            }
            await _next.Invoke(context);
        }
        private bool IsGitRoute(PathString routePath)
        {
            string[] pathParts = routePath.ToString().Split("/");
            if (pathParts != null && pathParts.Length >= 1)
            {
                return pathParts[1].EndsWith(".git");
            }
            return false;
        }
    }
     
    /// <summary>
    /// Extension wrapper for using Kube Middleware
    /// </summary>
    public static class KubeMiddlewareExtension
    {
        public static IApplicationBuilder UseKubeMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<KubeMiddleware>();
        }
    }
}
