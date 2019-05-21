using System.Threading.Tasks;
using Kudu.Core.Helpers;
using Kudu.Contracts.Settings;
using Kudu.Core.Deployment.Oryx;
using Kudu.Core.Infrastructure;
using System.IO;

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
                    Oryx.ExpressBuilder.SetupExpressBuilderArtifacts(context.OutputPath);
                }
            }

            return Task.CompletedTask;
        }

        public static void PreOryxBuild(DeploymentContext context)
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

        //public override void PostBuild(DeploymentContext context)
        //{
        //    // no-op
        //    context.Logger.Log($"Skipping post build. Project type: {ProjectType}");
        //    FileLogHelper.Log("Completed PostBuild oryx....");
        //}
    }
}
