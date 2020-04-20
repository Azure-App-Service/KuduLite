using Kudu.Contracts.Settings;

namespace Kudu.Core.Deployment.Generator
{
    public class NoOpBuilder : BaseBasicBuilder
    {
        public NoOpBuilder(IEnvironment environment, IDeploymentSettingsManager settings, IBuildPropertyProvider propertyProvider, string repositoryPath, string projectPath)
            : base(environment, settings, propertyProvider, repositoryPath, projectPath, "--basic")
        {
        }

        public override string ProjectType
        {
            get { return "NoOp"; }
        }
    }
}
