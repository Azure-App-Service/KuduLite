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
                // K8SE TODO : Move to constants
                homeDir = "C:\\repos\\apps\\";
                siteRepoDir = "\\site\\repository";
            }
            else
            {
                // K8SE TODO : Move to constants
                homeDir = "/home/apps/";
                siteRepoDir = "/site/repository";
            }

            // Cache the App Environment for this request
            if (!context.Items.ContainsKey("environment"))
            {
                Console.WriteLine("KubeMiddlware: adding environment. appName=" + appName + ",appType=" + appType);
                context.Items.TryAdd("environment", GetEnvironment(homeDir, appName, null, null, appNamenamespace, appType));
            }

            // Cache the appName for this request
            if (!context.Items.ContainsKey("appName")) {
                Console.WriteLine("KubeMiddlware: appName=" + appName);
                context.Items.TryAdd("appName", appName);
            }

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
            var root = KubeMiddleware.ResolveRootPath(home, appName);
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

        /// <summary>
        /// Resolves the root path for the app being served by
        /// Multitenant Kudu
        /// </summary>
        /// <param name="home"></param>
        /// <param name="appName"></param>
        /// <returns></returns>
        public static string ResolveRootPath(string home, string appName)
        {
            // The HOME path should always be set correctly
            //var path = System.Environment.ExpandEnvironmentVariables(@"%HOME%");
            var path = $"{home}{appName}";

            FileSystemHelpers.EnsureDirectory(path);
            FileSystemHelpers.EnsureDirectory($"{path}/site/artifacts/hostingstart");
            // For users running Windows Azure Pack 2 (WAP2), %HOME% actually points to the site folder,
            // which we don't want here. So yank that segment if we detect it.
            if (Path.GetFileName(path).Equals(Constants.SiteFolder, StringComparison.OrdinalIgnoreCase))
            {
                path = Path.GetDirectoryName(path);
            }

            return path;

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
