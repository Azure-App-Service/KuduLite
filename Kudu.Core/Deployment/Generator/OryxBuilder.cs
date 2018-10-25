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

            string framework = System.Environment.GetEnvironmentVariable("FRAMEWORK");
            string version = System.Environment.GetEnvironmentVariable("FRAMEWORK_VERSION");
            string deploymentTarget = "/home/site/wwwroot"; //System.Environment.GetEnvironmentVariable("$DEPLOYMENT_TARGET");

            string oryxLanguage = "";

            if (framework.StartsWith("NODE"))
            {
                oryxLanguage = "nodejs";
            }
            else if (framework.StartsWith("PYTHON"))
            {
                oryxLanguage = "python";
            }

            string oryxBuildCommand = string.Format("oryx build {0} {1} -l {2} --language-version {3}", deploymentTarget, deploymentTarget, oryxLanguage, version);

            FileLogHelper.Log("Running OryxBuild with  " + oryxBuildCommand);
            RunCommand(context, oryxBuildCommand, false, "Running oryx build...");

            FileLogHelper.Log("Completed oryx build...");

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