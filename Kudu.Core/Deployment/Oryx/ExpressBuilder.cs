using Kudu.Core.SourceControl;
using System;
using System.IO;

namespace Kudu.Core.Deployment.Oryx
{
    public class ExpressBuilder
    {
        public static void SetupExpressBuilderArtifacts(string outputPath)
        {
            string framework = System.Environment.GetEnvironmentVariable(OryxBuildConstants.OryxEnvVars.FrameworkSetting);

            if (string.IsNullOrEmpty(framework))
            {
                return;
            }

            Framework oryxFramework = SupportedFrameworks.ParseLanguage(framework);
            string root = "/home/data/SitePackages";
            string packageNameFile = Path.Combine(root, "packagename.txt");
            string packagePathFile = Path.Combine(root, "packagepath.txt");

            CreateSitePackagesDirectory(root);
            string packageName = "";
            if(oryxFramework == Framework.NodeJs)
            {
                // For App service express mode
                // Generate packagename.txt and packagepath
                packageName = "node_modules.zip:/node_modules";
            }
            else if(oryxFramework == Framework.Python)
            {
                // not supported
            }

            File.WriteAllText(packageNameFile, packageName);
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
