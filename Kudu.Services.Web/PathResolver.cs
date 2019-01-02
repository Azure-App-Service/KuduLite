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
    }
}