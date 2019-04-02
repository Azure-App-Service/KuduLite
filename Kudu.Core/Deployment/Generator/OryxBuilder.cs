using System.Threading.Tasks;
using Kudu.Core.Helpers;
using Kudu.Contracts.Settings;

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

            // Step 1: Run kudusync

            string kuduSyncCommand = string.Format("kudusync -v 50 -f {0} -t {1} -n {2} -p {3} -i \".git;.hg;.deployment;.deploy.sh\"",
                RepositoryPath,
                context.OutputPath,
                context.NextManifestFilePath,
                context.PreviousManifestFilePath
                );

            FileLogHelper.Log("Running KuduSync with  " + kuduSyncCommand);

            RunCommand(context, kuduSyncCommand, false, "Oryx-Build: Running kudu sync...");

            OryxArguments args = new OryxArguments();
            if (args.RunOryxBuild)
            {
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

        //public override void PostBuild(DeploymentContext context)
        //{
        //    // no-op
        //    context.Logger.Log($"Skipping post build. Project type: {ProjectType}");
        //    FileLogHelper.Log("Completed PostBuild oryx....");
        //}
    }
}
