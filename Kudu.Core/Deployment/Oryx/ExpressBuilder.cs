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
            //
            // Generate packagename.txt and packagepath
            string packagename = "nodemodules.zip:/node_modules";
            File.WriteAllText("/home/data/SitePackages/packagename.txt", packagename);
            File.WriteAllText("/home/data/SitePackages/packagepath.txt", outputPath);
        }
    }
}
