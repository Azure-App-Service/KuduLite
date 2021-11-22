using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using Kudu.Core.Infrastructure;
using Kudu.Contracts.Settings;
using Kudu.Services.Infrastructure;
using Kudu.Core;
using System.Text;
using Kudu.Core.Helpers;
using System.IO;
using System.Linq;
using Kudu.Core.K8SE;

namespace Kudu.Services.Web
{
    /// <summary>
    /// Middleware to modify Kudu Context when running on an K8 Cluster
    /// </summary>
    public class KubeMiddleware
    {
        private const string KuduConsoleFilename = "kudu.dll";
        private const string KuduConsoleRelativePath = "KuduConsole";
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
            string appName = K8SEDeploymentHelper.GetAppName(context);
            string appNamenamespace = K8SEDeploymentHelper.GetAppNamespace(context);
            string appType = K8SEDeploymentHelper.GetAppKind(context);

            string homeDir = "";
            string siteRepoDir = "";
            if (OSDetector.IsOnWindows())
            {
                homeDir = Constants.WindowsAppHomeDir;
                siteRepoDir = Constants.WindowsSiteRepoDir;
            }
            else
            {
                homeDir = Constants.LinuxAppHomeDir;
                siteRepoDir = Constants.LinuxSiteRepoDir;
            }

            //Use the appName from git as the real app name of the current git operation.
            string[] pathParts = context.Request.Path.ToString().Split("/");

            if (pathParts != null && pathParts.Length >= 1 && IsGitRoute(context.Request.Path))
            {
                appName = pathParts[1];
                appName = appName.Trim().Replace(".git", "");
                if (!FileSystemHelpers.DirectoryExists(homeDir + appName))
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("The repository does not exist", Encoding.UTF8);
                    return;
                }
            }

            serverConfig.GitServerRoot = appName + ".git";

            // TODO: Use Path.Combine
            environment.RepositoryPath = $"{homeDir}{appName}{siteRepoDir}";

            // Cache the App Environment for this request
            context.Items.TryAdd("environment", GetEnvironment(homeDir, appName, null, context, appNamenamespace, appType));

            // Cache the appName for this request
            context.Items.TryAdd("appName", appName);

            // Add All AppSettings to the context.
            K8SEDeploymentHelper.UpdateContextWithAppSettings(context);

            PodInstance instance = null;

            if (context.Request.Path.Value.StartsWith("/instances/", StringComparison.OrdinalIgnoreCase)
                && context.Request.Path.Value.IndexOf("/webssh") > 0)
            {
                List<PodInstance> instances = K8SEDeploymentHelper.GetInstances(appName);

                int idx = context.Request.Path.Value.IndexOf("/webssh");
                string instanceId = context.Request.Path.Value.Substring(0, idx).Replace("/instances/", "");
                Console.WriteLine($"\n\n\n\n inst id {instanceId}");
                if (instances.Count > 0)
                {
                    instance = instances.Where(i => i.Name.Equals(instanceId, System.StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                }

                if (instances.Count > 0 && instanceId.Equals("any", System.StringComparison.OrdinalIgnoreCase))
                {
                    instance = instances[0];
                }

                if (instance == null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync("Instance not found");
                    return;
                }

                int idx2 = context.Request.Path.Value.IndexOf("/webssh");
                context.Request.Path = context.Request.Path.Value.Substring(idx2);
                if (!context.Request.Headers.ContainsKey("WEBSITE_SSH_USER"))
                {
                    context.Request.Headers.Add("WEBSITE_SSH_USER", "root");
                }
                if (!context.Request.Headers.ContainsKey("WEBSITE_SSH_PASSWORD"))
                {
                    context.Request.Headers.Add("WEBSITE_SSH_PASSWORD", "Docker!");
                }
                if (!context.Request.Headers.ContainsKey("WEBSITE_SSH_IP"))
                {
                    context.Request.Headers.Add("WEBSITE_SSH_IP", instance.IpAddress);
                }
            }

            // Cache the appNamenamespace for this request if it's not empty or null
            if (!string.IsNullOrEmpty(appNamenamespace))
            {
                context.Items.TryAdd("appNamespace", appNamenamespace);
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

        /// <summary>
        /// Returns a specified environment configuration as the current webapp's
        /// default configuration during the runtime.
        /// </summary>
        private static IEnvironment GetEnvironment(
            string home,
            string appName,
            IDeploymentSettingsManager settings = null,
            HttpContext httpContext = null,
            string appNamespace = null,
            string appType = null)
        {
            var root = PathResolver.ResolveRootPath(home, appName);
            var siteRoot = Path.Combine(root, Constants.SiteFolder);
            var repositoryPath = Path.Combine(siteRoot,
                settings == null ? Constants.RepositoryPath : settings.GetRepositoryPath());
            var binPath = AppContext.BaseDirectory;
            var requestId = httpContext != null ? httpContext.Request.GetRequestId() : null;
            var kuduConsoleFullPath =
                Path.Combine(AppContext.BaseDirectory, KuduConsoleRelativePath, KuduConsoleFilename);
            return new Core.Environment(root, EnvironmentHelper.NormalizeBinPath(binPath), repositoryPath, requestId,
                kuduConsoleFullPath, null, appName, appNamespace, appType);
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
