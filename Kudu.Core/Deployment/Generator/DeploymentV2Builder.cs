using Kudu.Contracts.Settings;
using Kudu.Core.Deployment.Oryx;
using Kudu.Core.Infrastructure;
using System;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading.Tasks;
using Kudu.Core.Tracing;

namespace Kudu.Core.Deployment.Generator
{
    public class DeploymentV2Builder : ExternalCommandBuilder
    {
        private string repositoryPath;
        public DeploymentV2Builder(IEnvironment environment, IDeploymentSettingsManager settings, IBuildPropertyProvider propertyProvider, string sourcePath)
        : base(environment, settings, propertyProvider, sourcePath)
        {
            repositoryPath = sourcePath;
        }

        public override Task Build(DeploymentContext context)
        {
            KuduEventGenerator.Log().LogMessage(EventLevel.Informational, string.Empty,
                $"Starting {nameof(DeploymentV2Builder)}.{nameof(Build)}", string.Empty);
            return Task.Run(() => SetupAppServiceArtifacts(context));
        }

        public override string ProjectType
        {
            get { return "Basic+DeploymentV2"; }
        }

        private void SetupAppServiceArtifacts(DeploymentContext context)
        {
            string sitePackages = "/home/data/SitePackages";
            string deploymentsPath = $"/home/site/deployments/";
            string artifactPath = $"/home/site/deployments/{context.CommitId}/artifact";
            string packageNameFile = Path.Combine(sitePackages, "packagename.txt");
            string packagePathFile = Path.Combine(sitePackages, "packagepath.txt");

            FileSystemHelpers.EnsureDirectory(sitePackages);
            FileSystemHelpers.EnsureDirectory(artifactPath);

            string zipAppName = $"{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.zip";
            context.Logger.Log($"Repository path is {repositoryPath}");
            string createdZip = PackageArtifactFromFolder(context, repositoryPath,
                repositoryPath, zipAppName, BuildArtifactType.Zip, numBuildArtifacts: -1);
            context.Logger.Log($"Copying the deployment artifact to {zipAppName} file");
            var copyExe = ExternalCommandFactory.BuildExternalCommandExecutable(repositoryPath, artifactPath, context.Logger);
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

            // Gotta remove the old zips
            DeploymentHelper.PurgeOldDeploymentsIfNecessary(deploymentsPath, context.Tracer, totalAllowedDeployments: 10);

            File.WriteAllText(packageNameFile, zipAppName);
            File.WriteAllText(packagePathFile, artifactPath);
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
            context.Logger.Log($"Packaged zip file {file}");
            return file;
        }
    }
}
