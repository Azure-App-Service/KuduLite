using Kudu.Core.Deployment;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage;
using Kudu.Core.Deployment.Oryx;
using Kudu.Core.Infrastructure;
using Kudu.Contracts.Settings;
using System.IO;
using Kudu.Core.Deployment.Generator;

namespace Kudu.Core.Helpers
{
    public class LinuxConsumptionDeploymentHelper
    {
        /// <summary>
        /// Specifically used for Linux Consumption to support Server Side build scenario
        /// </summary>
        /// <param name="context"></param>
        public static async Task SetupLinuxConsumptionFunctionAppDeployment(IEnvironment env, IDeploymentSettingsManager settings, DeploymentContext context)
        {
            string sas = System.Environment.GetEnvironmentVariable(Constants.ScmRunFromPackage);
            string builtFolder = context.OutputPath;
            string packageFolder = env.DeploymentsPath;
            string packageFileName = OryxBuildConstants.FunctionAppBuildSettings.LinuxConsumptionArtifactName;

            // Package built content from oryx build artifact
            string filePath = PackageArtifactFromFolder(env, settings, context, builtFolder, packageFolder, packageFileName);

            // Upload from DeploymentsPath
            await UploadLinuxConsumptionFunctionAppBuiltContent(context, sas, filePath);

            // Clean up local built content
            FileSystemHelpers.DeleteDirectoryContentsSafe(context.OutputPath);

            // Remove Linux consumption plan functionapp workers for the site
            await RemoveLinuxConsumptionFunctionAppWorkers(context);
        }

        public static async Task UploadLinuxConsumptionFunctionAppBuiltContent(DeploymentContext context, string sas, string filePath)
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
            }
            catch (StorageException se)
            {
                context.Logger.Log($"Failed to upload because Azure Storage responds {se.RequestInformation.HttpStatusCode}.");
                context.Logger.Log(se.Message);
                throw new DeploymentFailedException(se);
            }
        }

        public static async Task RemoveLinuxConsumptionFunctionAppWorkers(DeploymentContext context)
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

        private static string PackageArtifactFromFolder(IEnvironment environment, IDeploymentSettingsManager settings, DeploymentContext context, string srcDirectory, string artifactDirectory, string artifactFilename)
        {
            context.Logger.Log("Writing the artifacts to a squashfs file");
            string file = Path.Combine(artifactDirectory, artifactFilename);
            ExternalCommandFactory commandFactory = new ExternalCommandFactory(environment, settings, context.RepositoryPath);
            Executable exe = commandFactory.BuildExternalCommandExecutable(srcDirectory, artifactDirectory, context.Logger);
            try
            {
                exe.ExecuteWithProgressWriter(context.Logger, context.Tracer, $"mksquashfs . {file} -noappend");
            }
            catch (Exception)
            {
                context.GlobalLogger.LogError();
                throw;
            }

            int numOfArtifacts = OryxBuildConstants.FunctionAppBuildSettings.ConsumptionBuildMaxFiles;
            DeploymentHelper.PurgeBuildArtifactsIfNecessary(artifactDirectory, BuildArtifactType.Squashfs, context.Tracer, numOfArtifacts);
            return file;
        }
    }
}
