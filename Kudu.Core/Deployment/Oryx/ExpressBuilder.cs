using System;
using System.IO;

namespace Kudu.Core.Deployment.Oryx
{
    public class ExpressBuilder
    {
        public static void SetupExpressBuilderArtifacts(string outputPath)
        {
            string root = "/home/data/SitePackages";
            string packageNameFile = Path.Combine(root, "packagename.txt");
            string packagePathFile = Path.Combine(root, "packagepath.txt");

            CreateSitePackagesDirectory(root);

            // For App service express mode
            // Generate packagename.txt and packagepath
            string packagename = "node_modules.zip:/node_modules";

            File.WriteAllText(packageNameFile, packagename);
            File.WriteAllText(packagePathFile, outputPath);
        }

        private static void CreateSitePackagesDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}
