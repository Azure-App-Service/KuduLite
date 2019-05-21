using System.IO;
using System.IO.Compression;
using Kudu.Core.Infrastructure;

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

            if (FunctionAppHelper.LooksLikeFunctionApp())
            {
                // For function app express mode
                SetupFunctionApp(root, packageNameFile, outputPath);
            }
            else
            {
                // For App service express mode
                // Generate packagename.txt and packagepath
                string packagename = "node_modules.zip:/node_modules";

                File.WriteAllText(packageNameFile, packagename);
                File.WriteAllText(packagePathFile, outputPath);
            }
        }

        private static void SetupFunctionApp(string root, string packageNameFile, string outputPath)
        {
            string zipAppName = "functionapp.zip";
            string zipFilePath = Path.Combine(root, zipAppName);

            if (File.Exists(zipFilePath))
            {
                File.Delete(zipFilePath);
            }

            ZipFile.CreateFromDirectory(outputPath, zipFilePath);
            File.WriteAllText(packageNameFile, zipAppName);
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
