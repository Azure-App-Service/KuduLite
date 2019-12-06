using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using Kudu.Core.Infrastructure;
using Kudu.Contracts.Settings;
using Kudu.Services.Infrastructure;
using Kudu.Core;
using System.Text;
using Kudu.Core.Helpers;
using System.IO;
using System.Net.Http.Headers;

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
            var result = await context.AuthenticateAsync();
         
            if (result.Succeeded == false)
            {
                context.Response.StatusCode = 401;
                context.Response.Headers["WWW-Authenticate"] = "Basic realm=\"site\"";
                return;
            }

            var host = context.Request.Headers["Host"].ToString();
            Console.WriteLine("HOST: "+host);
            var appName = "";

            if (host.IndexOf(".") <= 0)
            {
                appName = "test1";
            }
            else
            {
                try
                {
                    int res = Int32.Parse(host.Substring(0, host.IndexOf(".")));
                    var authHeader = AuthenticationHeaderValue.Parse(context.Request.Headers["Authorization"]);
                    var credentialBytes = Convert.FromBase64String(authHeader.Parameter);
                    var credentials = Encoding.UTF8.GetString(credentialBytes).Split(new[] { ':' }, 2);
                    var username = credentials[0];
                    appName = username;
                }
                catch (FormatException)
                {
                    appName = host.Substring(0, host.IndexOf("."));
                }
            }

            Console.WriteLine("AppName: " + appName);
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

            context.Items.Add("environment", GetEnvironment(homeDir, appName));


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
            environment.RepositoryPath = homeDir + appName + siteRepoDir;
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
            HttpContext httpContext = null)
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
                kuduConsoleFullPath, null);
        }

        public static string ResolveRootPath(string home, string appName)
        {
            // The HOME path should always be set correctly
            //var path = System.Environment.ExpandEnvironmentVariables(@"%HOME%");
            var path = home + appName;
            Console.WriteLine("Checking site home : " + (home + appName));
            if (!Directory.Exists(path))
                // We should never get here
                throw new DirectoryNotFoundException("The site's home directory could not be located");
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
