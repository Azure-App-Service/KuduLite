using Kudu.Contracts.Settings;
using Kudu.Core.Deployment.Oryx;
using Kudu.Core.Infrastructure;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Kudu.Core.Deployment.Generator
{
    public class BasicBuilder : BaseBasicBuilder
    {
        IEnvironment _environment;
        public BasicBuilder(IEnvironment environment, IDeploymentSettingsManager settings, IBuildPropertyProvider propertyProvider, string repositoryPath, string projectPath)
            : base(environment, settings, propertyProvider, repositoryPath, projectPath, "--basic")
        {
            _environment = environment;
        }

        public override Task Build(DeploymentContext context)
        {
            ILogger customLogger = context.Logger.Log("In K8 Basic Builder");
            context.Logger.Log("RepositoryPath "+ _environment.ZipTempPath);
            context.Logger.Log("SiteRootPath " + _environment.SiteRootPath);
            //context.Logger.Log("CurrId "+ _environment.);
            string src = _environment.RepositoryPath;
            string artifactDir = Path.Combine(_environment.SiteRootPath, "artifacts", _environment.CurrId);
            FileSystemHelpers.EnsureDirectory(Path.Combine(_environment.SiteRootPath, "artifacts"));
            FileSystemHelpers.EnsureDirectory(artifactDir);
            context.Logger.Log($"src : {src} dest {artifactDir}");
            return Task.Factory.StartNew(() => PackageArtifactFromFolder(context, _environment.ZipTempPath, Path.Combine(_environment.SiteRootPath, "atifacts", _environment.CurrId),"artifact.zip" ,BuildArtifactType.Squashfs, 2));
        }


        private string PackageArtifactFromFolder(DeploymentContext context, string srcDirectory, string artifactDirectory, string artifactFilename, BuildArtifactType artifactType, int numBuildArtifacts = 0)
        {
            context.Logger.Log($"Writing the artifacts to {artifactType.ToString()} file at {artifactDirectory}");
            string file = Path.Combine(artifactDirectory, artifactFilename);
            var exe = ExternalCommandFactory.BuildExternalCommandExecutable(srcDirectory, artifactDirectory, context.Logger);
            try
            {
                switch (artifactType)
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

        public override string ProjectType
        {
            get { return "BASIC"; }
        }
    }
}
