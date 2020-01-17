using System;
using System.IO;
using System.Threading.Tasks;
using Kudu.Core.Infrastructure;
using Kudu.Core.Helpers;
using Kudu.Contracts.Settings;
using Kudu.Core.Deployment.Oryx;

namespace Kudu.Core.Deployment.Generator
{
    public class OryxBuilder : ExternalCommandBuilder
    {
        public override string ProjectType => "Oryx-Build";

        IEnvironment environment;
        IDeploymentSettingsManager settingsManager;
        IBuildPropertyProvider propertyProvider;
        DeploymentInfoBase deploymentInfo;
        string sourcePath;

        public OryxBuilder(IEnvironment environment, IDeploymentSettingsManager settingsManager, IBuildPropertyProvider propertyProvider, DeploymentInfoBase deploymentInfo, string sourcePath)
            : base(environment, settingsManager, propertyProvider, sourcePath)
        {
            this.environment = environment;
            this.settingsManager = settingsManager;
            this.propertyProvider = propertyProvider;
            this.deploymentInfo = deploymentInfo;
            this.sourcePath = sourcePath;
        }

        public override Task Build(DeploymentContext context)
        {
            FileLogHelper.Log("In oryx build...");

            // initialize the repository Path for the build
            context.RepositoryPath = RepositoryPath;

            context.Logger.Log("Repository path is "+context.RepositoryPath);

            // Initialize Oryx Args.
            IOryxArguments args = OryxArgumentsFactory.CreateOryxArguments(environment, settingsManager);

            if (!args.SkipKuduSync)
            {
                // Step 1: Run kudusync
                string kuduSyncCommand = string.Format("kudusync -v 50 -f {0} -t {1} -n {2} -p {3} -i \".git;.hg;.deployment;.deploy.sh\"",
                    context.RepositoryPath,
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

                // Run express build setups if needed
                if (args.Flags == BuildOptimizationsFlags.UseExpressBuild)
                {
                    if (FunctionAppHelper.LooksLikeFunctionApp())
                    {
                        SetupFunctionAppExpressArtifacts(context);
                    }
                    else
                    {
                        ExpressBuilder appServiceExpressBuilder = new ExpressBuilder(environment, settingsManager, propertyProvider, sourcePath);
                        appServiceExpressBuilder.SetupExpressBuilderArtifacts(context.OutputPath, context, args);
                    }
                }
                // Publish API with RFP follows DeploymentV2 Format
                else if(args.Flags == BuildOptimizationsFlags.DeploymentV2 || (deploymentInfo.IsPublishRequest && deploymentInfo.ShouldRunArtifactFromPackage))
                {
                    SetupAppServiceArtifacts(context);
                }
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

        private void SetupAppServiceArtifacts(DeploymentContext context)
        {
            string deploymentsPath = environment.DeploymentsPath;
            string artifactPath = Path.Combine(deploymentsPath,context.CommitId,"artifact");

            FileSystemHelpers.EnsureDirectory(artifactPath);

            string zipAppName = $"{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.zip";

            string createdZip = PackageArtifactFromFolder(context, context.BuildTempPath,
                context.BuildTempPath, zipAppName, BuildArtifactType.Zip, numBuildArtifacts: -1);
            var copyExe = ExternalCommandFactory.BuildExternalCommandExecutable(context.BuildTempPath, artifactPath, context.Logger);
            var copyToPath = Path.Combine(artifactPath, zipAppName);

            try
            {
                copyExe.ExecuteWithProgressWriter(context.Logger, context.Tracer, $"cp {createdZip} {copyToPath}");
            }
            catch (Exception)
            {
                context.GlobalLogger.LogError();
                throw;
            }

            // Update the packagename file to ensure latest app restart loads the new zip file
            DeploymentHelper.UpdateLatestAndPurgeOldArtifacts(environment, zipAppName, artifactPath, context.Tracer);
        }


        private void SetupFunctionAppExpressArtifacts(DeploymentContext context)
        {
            string sitePackages = environment.SitePackagesPath;
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

            // Purge old zips
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
            }
            catch (Exception)
            {
                context.GlobalLogger.LogError();
                throw;
            }

            // Just to be sure that we don't keep adding build artifacts here
            if (numBuildArtifacts > 0)
            {
                DeploymentHelper.PurgeBuildArtifactsIfNecessary(artifactDirectory, artifactType, context.Tracer, numBuildArtifacts);
            }

            return file;
        }
    }
}
