using System;
using System.IO;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using Kudu.Core.Infrastructure;
using Kudu.Core.Helpers;
using Kudu.Contracts.Settings;
using Kudu.Core.Deployment.Oryx;

namespace Kudu.Core.Deployment.Generator
{
    public class OryxBuilder : ExternalCommandBuilder
    {
        public override string ProjectType => "Oryx-Build";

        public OryxBuilder(IEnvironment environment, IDeploymentSettingsManager settings, IBuildPropertyProvider propertyProvider, string sourcePath)
            : base(environment, settings, propertyProvider, sourcePath)
        {
        }

        public override Task Build(DeploymentContext context)
        {
            FileLogHelper.Log("In oryx build...");

            // initialize the repository Path for the build
            context.RepositoryPath = RepositoryPath;

            // Initialize Oryx Args.
            IOryxArguments args = OryxArgumentsFactory.CreateOryxArguments();

            if (!args.SkipKuduSync)
            {
                // Step 1: Run kudusync
                string kuduSyncCommand = string.Format("kudusync -v 50 -f {0} -t {1} -n {2} -p {3} -i \".git;.hg;.deployment;.deploy.sh\"",
                    RepositoryPath,
                    context.OutputPath,
                    context.NextManifestFilePath,
                    context.PreviousManifestFilePath
                    );

                FileLogHelper.Log("Running KuduSync with  " + kuduSyncCommand);

                RunCommand(context, kuduSyncCommand, false, "Oryx-Build: Running kudu sync...");
            }

            if (args.RunOryxBuild)
            {
                PreOryxBuild(context);
                
                string buildCommand = args.GenerateOryxBuildCommand(context);
                RunCommand(context, buildCommand, false, "Running oryx build...");

                //
                // Run express build setups if needed
                if (args.Flags == BuildOptimizationsFlags.UseExpressBuild)
                {
                    if (FunctionAppHelper.LooksLikeFunctionApp())
                    {
                        SetupFunctionAppExpressArtifacts(context);
                    }
                    else
                    {
                        Oryx.ExpressBuilder.SetupExpressBuilderArtifacts(context.OutputPath);
                    }
                }
            }

            // Detect if package upload is necessary for server side build
            if (FunctionAppHelper.HasScmRunFromPackage() && FunctionAppHelper.LooksLikeFunctionApp())
            {
                SetupLinuxConsumptionFunctionAppDeployment(context).Wait();
            }

            return Task.CompletedTask;
        }

        private static void PreOryxBuild(DeploymentContext context)
        {
            if (FunctionAppHelper.LooksLikeFunctionApp())
            {
                // We need to delete this directory in order to avoid issues with
                // reinstalling Python dependencies on a target directory
                var pythonPackagesDir = Path.Combine(context.OutputPath, ".python_packages");
                if (Directory.Exists(pythonPackagesDir))
                {
                    FileSystemHelpers.DeleteDirectorySafe(pythonPackagesDir);
                }
            }
        }

        private void SetupFunctionAppExpressArtifacts(DeploymentContext context)
        {
            string sitePackages = "/home/data/SitePackages";
            string packageNameFile = Path.Combine(sitePackages, "packagename.txt");
            string packagePathFile = Path.Combine(sitePackages, "packagepath.txt");

            FileSystemHelpers.EnsureDirectory(sitePackages);

            string zipAppName = $"{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.zip";
            string createdZip = PackageArtifactFromFolder(context, OryxBuildConstants.FunctionAppBuildSettings.ExpressBuildSetup,
                OryxBuildConstants.FunctionAppBuildSettings.ExpressBuildSetup, zipAppName, zipQuota: -1);

            var copyExe = ExternalCommandFactory.BuildExternalCommandExecutable(OryxBuildConstants.FunctionAppBuildSettings.ExpressBuildSetup, sitePackages, context.Logger);
            var copyToPath = Path.Combine(sitePackages, zipAppName);
            try
            {
                copyExe.ExecuteWithProgressWriter(context.Logger, context.Tracer, $"cp {createdZip} {copyToPath}");
            }
            catch (Exception)
            {
                context.GlobalLogger.LogError();
                throw;
            }

            // Gotta remove the old zips
            DeploymentHelper.PurgeZipsIfNecessary(sitePackages, context.Tracer, totalAllowedZips: 2);

            File.WriteAllText(packageNameFile, zipAppName);
            File.WriteAllText(packagePathFile, sitePackages);
        }

        private string PackageArtifactFromFolder(DeploymentContext context, string srcDirectory, string destDirectory, string destFilename, int zipQuota = 0)
        {
            context.Logger.Log("Writing the artifacts to a zip file");
            string zipFile = Path.Combine(destDirectory, destFilename);
            var exe = ExternalCommandFactory.BuildExternalCommandExecutable(srcDirectory, destDirectory, context.Logger);
            try
            {
                exe.ExecuteWithProgressWriter(context.Logger, context.Tracer, $"zip -r -0 -q {zipFile} .", String.Empty);
            }
            catch (Exception)
            {
                context.GlobalLogger.LogError();
                throw;
            }

            // Just to be sure that we don't keep adding zip files here
            if (zipQuota > 0)
            {
                DeploymentHelper.PurgeZipsIfNecessary(destDirectory, context.Tracer, totalAllowedZips: zipQuota);
            }

            return zipFile;
        }

        /// <summary>
        /// Specifically used for Linux Consumption to support Server Side build scenario
        /// </summary>
        /// <param name="context"></param>
        private async Task SetupLinuxConsumptionFunctionAppDeployment(DeploymentContext context)
        {
            string sas = System.Environment.GetEnvironmentVariable(Constants.ScmRunFromPackage);
            string builtFolder = context.RepositoryPath;
            string packageFolder = Environment.DeploymentsPath;
            string packageFileName = $"{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.zip";

            // Package built content from oryx build artifact
            string filePath = PackageArtifactFromFolder(context, builtFolder, packageFolder, packageFileName);

            // Upload from DeploymentsPath
            await UploadLinuxConsumptionFunctionAppBuiltContent(context, sas, filePath);

            // Remove Linux consumption plan functionapp workers for the site
            await RemoveLinuxConsumptionFunctionAppWorkers(context);
        }

        //public override void PostBuild(DeploymentContext context)
        //{
        //    // no-op
        //    context.Logger.Log($"Skipping post build. Project type: {ProjectType}");
        //    FileLogHelper.Log("Completed PostBuild oryx....");
        //}

        private async Task UploadLinuxConsumptionFunctionAppBuiltContent(DeploymentContext context, string sas, string filePath)
        {
            context.Logger.Log($"Uploading built content {filePath} -> {sas}");

            // Check if SCM_RUN_FROM_PACKAGE does exist
            if (string.IsNullOrEmpty(sas))
            {
                context.Logger.Log($"Failed to upload because SCM_RUN_FROM_PACKAGE is not provided.");
                throw new DeploymentFailedException(new ArgumentException("Failed to upload because SAS is empty."));
            }

            // Parse SAS
            Uri sasUri = null;
            if (!Uri.TryCreate(sas, UriKind.Absolute, out sasUri))
            {
                context.Logger.Log($"Malformed SAS when uploading built content.");
                throw new DeploymentFailedException(new ArgumentException("Failed to upload because SAS is malformed."));
            }

            // Upload blob to Azure Storage
            CloudBlockBlob blob = new CloudBlockBlob(sasUri);
            try
            {
                await blob.UploadFromFileAsync(filePath);
            } catch (StorageException se)
            {
                context.Logger.Log($"Failed to upload because Azure Storage responds {se.RequestInformation.HttpStatusCode}.");
                context.Logger.Log(se.Message);
                throw new DeploymentFailedException(se);
            }
        }

        private async Task RemoveLinuxConsumptionFunctionAppWorkers(DeploymentContext context)
        {
            string webSiteHostName = System.Environment.GetEnvironmentVariable(SettingsKeys.WebsiteHostname);
            string sitename = ServerConfiguration.GetApplicationName();

            context.Logger.Log($"Reseting all workers for {webSiteHostName}");

            try
            {
                await OperationManager.AttemptAsync(async () =>
                {
                    await PostDeploymentHelper.RemoveAllWorkersAsync(webSiteHostName, sitename);
                }, retries: 3, delayBeforeRetry: 2000);
            }
            catch (ArgumentException ae)
            {
                context.Logger.Log($"Reset all workers has malformed webSiteHostName or sitename {ae.Message}");
                throw new DeploymentFailedException(ae);
            }
            catch (HttpRequestException hre)
            {
                context.Logger.Log($"Reset all workers endpoint responded with {hre.Message}");
                throw new DeploymentFailedException(hre);
            }
        }
    }
}
