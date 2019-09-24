using Kudu.Contracts.Settings;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Infrastructure;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Kudu.Core.Deployment.Oryx
{
    public class ExpressBuilder : ExternalCommandBuilder
    {
        public override string ProjectType => "OryxBuild";
        public IEnvironment environment;
        IDeploymentSettingsManager settings;
        IBuildPropertyProvider propertyProvider;

        public ExpressBuilder(IEnvironment environment, IDeploymentSettingsManager settings, IBuildPropertyProvider propertyProvider, string sourcePath)
            : base(environment, settings, propertyProvider, sourcePath)
        {
            this.environment = environment;
            this.settings = settings;
        }

        public void SetupExpressBuilderArtifacts(string outputPath, DeploymentContext context, IOryxArguments args)
        {     
            if(args.Flags != BuildOptimizationsFlags.UseExpressBuild)
            {
                return;
            }

            string sitePackagesDir = "/home/data/SitePackages";
            string packageNameFile = Path.Combine(sitePackagesDir, "packagename.txt");
            string packagePathFile = Path.Combine(sitePackagesDir, "packagepath.txt");

            FileSystemHelpers.EnsureDirectory(sitePackagesDir);

            string packageName = "";

            if(args.Language == Framework.NodeJs)
            {
                // For App service express mode
                // Generate packagename.txt and packagepath
                //packageName = "node_modules.zip:/node_modules";
                SetupNodeAppExpressArtifacts(context, sitePackagesDir, outputPath);
            }
            else if(args.Language == Framework.Python)
            {
                packageName = $"{args.VirtualEnv}.zip:/home/site/wwwroot/{args.VirtualEnv}";
            }
            else if(args.Language == Framework.DotNETCore)
            {
                // store the zipped artifacts at site packages dir
                string artifactName = SetupNetCoreAppExpressArtifacts(context, sitePackagesDir, outputPath);
                packageName = $"{artifactName:/home/site/wwwroot}";
            }

            File.WriteAllText(packageNameFile, packageName);
            File.WriteAllText(packagePathFile, outputPath);
        }

        private string SetupNetCoreAppExpressArtifacts(DeploymentContext context, string sitePackages,string outputPath)
        {
            context.Logger.Log($"Express Build enabled for NETCore app");

            // Create NetCore Zip at tm build folder where artifact were build and copy it to sitePackages
            string zipAppName = $"{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.zip";

            string createdZip = PackageArtifactFromFolder(context, context.BuildTempPath, outputPath, zipAppName, BuildArtifactType.Zip, numBuildArtifacts: -1);

            // Remove the old zips
            DeploymentHelper.PurgeBuildArtifactsIfNecessary(sitePackages, BuildArtifactType.Zip, context.Tracer, totalAllowedFiles: 2);

            return zipAppName;
        }


        private string SetupNodeAppExpressArtifacts(DeploymentContext context, string sitePackages, string outputPath)
        {
            context.Logger.Log($"Express Build enabled for Node app");

            // Create NetCore Zip at tm build folder where artifact were build and copy it to sitePackages, .GetBranch()
            string zipAppName = $"{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.zip";

            context.Logger.Log($"From {context.BuildTempPath} to {(environment.RepositoryPath + "../artifacts/" + environment.CurrId)} ");
            FileSystemHelpers.EnsureDirectory(environment.RepositoryPath + "../artifacts/");
            FileSystemHelpers.EnsureDirectory(environment.RepositoryPath + "../artifacts/" + environment.CurrId);
            string createdZip = PackageArtifactFromFolder(context, context.BuildTempPath, environment.RepositoryPath +"../artifacts/" +environment.CurrId, zipAppName, BuildArtifactType.Squashfs, numBuildArtifacts: -1);

            // Remove the old zips
            DeploymentHelper.PurgeBuildArtifactsIfNecessary(sitePackages, BuildArtifactType.Zip, context.Tracer, totalAllowedFiles: 2);

            return zipAppName;
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

        public override Task Build(DeploymentContext context)
        {
            throw new NotImplementedException();
        }
    }
}
