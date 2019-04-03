using System;
using System.Collections.Generic;
using System.Text;
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

            //
            // Generate packagename.txt and packagepath
            string packagename = "nodemodules.zip:/node_modules";

            CreateSitePackagesDirectory(root);

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
