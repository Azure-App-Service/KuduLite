using Kudu.Core.Infrastructure;
using System;
using System.IO;

namespace Kudu.Services.Web
{
    public static class PathResolver
    {
        public static string ResolveRootPath()
        {
            // The HOME path should always be set correctly
            var path = Environment.ExpandEnvironmentVariables(@"%HOME%");
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
}