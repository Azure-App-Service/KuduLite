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
using Kudu.Core.Tracing;

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

                try
                {
                    RunCommand(context, buildCommand, false, "Running oryx build...");
                    EmitKuduLog("OryxBuildStage='{0}' OryxBuildResult='{1}'", buildCommand, "SUCCEEDED");
                } catch
                {
                    EmitKuduLog("OryxBuildStage='{0}' OryxBuildResult='{1}'", buildCommand, "FAILED");
                    throw;
                }

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
                OryxBuildConstants.FunctionAppBuildSettings.ExpressBuildSetup, zipAppName, BuildArtifactType.Zip, numBuildArtifacts: -1);

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
            DeploymentHelper.PurgeBuildArtifactsIfNecessary(sitePackages, BuildArtifactType.Zip, context.Tracer, totalAllowedFiles: 2);

            File.WriteAllText(packageNameFile, zipAppName);
            File.WriteAllText(packagePathFile, sitePackages);
        }

        /// <summary>
        /// Package every files and sub directories from a source folder
        /// </summary>
        /// <param name="context">The deployment context in current scope</param>
        /// <param name="srcDirectory">The source directory to be packed</param>
        /// <param name="artifactDirectory">The destination directory to eject the build artifact</param>
        /// <param name="artifactFilename">The filename of the build artifact</param>
        /// <param name="artifactType">The method for packing the artifact</param>
        /// <param name="numBuildArtifacts">The number of temporary artifacts should be hold in the destination directory</param>
        /// <returns></returns>
        private string PackageArtifactFromFolder(DeploymentContext context, string srcDirectory, string artifactDirectory, string artifactFilename, BuildArtifactType artifactType, int numBuildArtifacts = 0)
        {
            context.Logger.Log($"Writing the artifacts to a {artifactType.ToString()} file");
            string file = Path.Combine(artifactDirectory, artifactFilename);
            var exe = ExternalCommandFactory.BuildExternalCommandExecutable(srcDirectory, artifactDirectory, context.Logger);
            try
            {
                switch(artifactType)
                {
                    case BuildArtifactType.Zip:
                        exe.ExecuteWithProgressWriter(context.Logger, context.Tracer, $"zip -r -0 -q {file} .");
                        break;
                    case BuildArtifactType.Squashfs:
                        exe.ExecuteWithProgressWriter(context.Logger, context.Tracer, $"mksquashfs . {file} -noappend");
                        break;
                    default:
                        throw new ArgumentException($"Received unknown file extension {artifactType.ToString()}");
                }
                EmitKuduLog("OryxBuildStage='Package Built Artifact {0}' OryxBuildResult='{1}'", file, "SUCCEEDED");
            }
            catch (Exception)
            {
                context.GlobalLogger.LogError();
                EmitKuduLog("OryxBuildStage='Package Built Artifact {0}' OryxBuildResult='{1}'", file, "FAILED");
                throw;
            }

            // Just to be sure that we don't keep adding build artifacts here
            if (numBuildArtifacts > 0)
            {
                DeploymentHelper.PurgeBuildArtifactsIfNecessary(artifactDirectory, artifactType, context.Tracer, numBuildArtifacts);
            }

            return file;
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
            string packageFileName = $"{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.squashfs";

            // Package built content from oryx build artifact
            string filePath = PackageArtifactFromFolder(context, builtFolder, packageFolder, packageFileName, BuildArtifactType.Squashfs, 
                OryxBuildConstants.FunctionAppBuildSettings.ConsumptionBuildMaxFiles);

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
                EmitKuduLog("OryxBuildStage='{0}' OryxBuildResult='{1}'", "Upload Built Artifact", "SUCCEEDED");
            } catch (StorageException se)
            {
                context.Logger.Log($"Failed to upload because Azure Storage responds {se.RequestInformation.HttpStatusCode}.");
                context.Logger.Log(se.Message);
                EmitKuduLog("OryxBuildStage='{0}' OryxBuildResult='{1}'", "Upload Built Artifact", "FAILED");
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
                EmitKuduLog("OryxBuildStage='Restart Function Host Worker {0}' OryxBuildResult='{1}'", sitename, "SUCCEEDED");
            }
            catch (ArgumentException ae)
            {
                context.Logger.Log($"Reset all workers has malformed webSiteHostName or sitename {ae.Message}");
                EmitKuduLog("OryxBuildStage='Restart Function Host Worker {0}' OryxBuildResult='{1}'", sitename, "FAILED");
                throw new DeploymentFailedException(ae);
            }
            catch (HttpRequestException hre)
            {
                context.Logger.Log($"Reset all workers endpoint responded with {hre.Message}");
                EmitKuduLog("OryxBuildStage='Restart Function Host Worker {0}' OryxBuildResult='{1}'", sitename, "FAILED");
                throw new DeploymentFailedException(hre);
            }
        }

        private void EmitKuduLog(string format, params string[] args)
        {
            KuduEventGenerator.Log().GenericEvent(
                ServerConfiguration.GetApplicationName(),
                string.Format(format, args),
                Guid.Empty.ToString(),
                string.Empty,
                string.Empty,
                string.Empty);
        }
    }
}
