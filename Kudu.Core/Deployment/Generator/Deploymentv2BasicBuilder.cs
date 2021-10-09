using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core.Infrastructure;
using Kudu.Core.SourceControl;
using System;
using System.IO;

namespace Kudu.Core.Deployment.Generator
{
    class DeploymentV2BasicBuilder : BaseBasicBuilder
    {
        private readonly IEnvironment _environment;
        private readonly ILogger _logger;
        private readonly ITracer _tracer;
        private readonly IRepository _repository;

        public DeploymentV2BasicBuilder(IEnvironment environment, 
            IDeploymentSettingsManager settings, 
            IBuildPropertyProvider propertyProvider, 
            DeploymentInfoBase deploymentInfo,
            IRepository repository,
            ILogger logger,
            ITracer tracer,
            string repositoryPath, 
            string projectPath)
            : base(environment, settings, propertyProvider, repositoryPath, projectPath, "--basic")
        {
            this._environment = environment;
            this._logger = logger;
            this._tracer = tracer;
            this._repository = repository;
            if(deploymentInfo is ZipDeploymentInfo)
            {
                var zipDeploymentInfo = ((ZipDeploymentInfo)deploymentInfo);
                SetupArtifacts(zipDeploymentInfo);
            }
        }

        public override string ProjectType
        {
            get { return "BASICV2"; }
        }

        private void SetupArtifacts(ZipDeploymentInfo deploymentInfo)
        {
            string deploymentsPath = _environment.DeploymentsPath;
            string artifactPath = Path.Combine(deploymentsPath, _repository.CurrentId, "artifact");

            FileSystemHelpers.EnsureDirectory(artifactPath);

            string zipAppName = $"{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.zip";

            var copyExe = ExternalCommandFactory.BuildExternalCommandExecutable(_environment.ZipTempPath, artifactPath, _logger);
            var copyToPath = Path.Combine(artifactPath, zipAppName);

            try
            {
                copyExe.ExecuteWithProgressWriter(_logger, _tracer, $"cp {deploymentInfo.RepositoryUrl} {copyToPath}");
            }
            catch (Exception)
            {
                throw;
            }

            // Update the packagename file to ensure latest app restart loads the new zip file
            // K8SE TODO: Uncomment this and test
            //DeploymentHelper.UpdateLatestAndPurgeOldArtifacts(_environment, zipAppName, artifactPath, _tracer);
        }
    }
}
