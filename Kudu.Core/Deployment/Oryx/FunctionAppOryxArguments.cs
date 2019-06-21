using Kudu.Core.Infrastructure;
using System.IO;
using System.Text;

namespace Kudu.Core.Deployment.Oryx
{
    public class FunctionAppOryxArguments : IOryxArguments
    {
        public bool RunOryxBuild { get; set; }

        public BuildOptimizationsFlags Flags { get; set; }

        protected readonly WorkerRuntime FunctionsWorkerRuntime;
        public bool SkipKuduSync { get; set; }

        public FunctionAppOryxArguments()
        {
            SkipKuduSync = false;
            FunctionsWorkerRuntime = ResolveWorkerRuntime();
            RunOryxBuild = FunctionsWorkerRuntime != WorkerRuntime.None;
            var buildFlags = GetEnvironmentVariableOrNull(OryxBuildConstants.OryxEnvVars.BuildFlagsSetting);
            Flags = BuildFlagsHelper.Parse(buildFlags);
            SkipKuduSync = Flags == BuildOptimizationsFlags.UseExpressBuild;
        }

        public virtual string GenerateOryxBuildCommand(DeploymentContext context)
        {
            StringBuilder args = new StringBuilder();

            AddOryxBuildCommand(args, context, source: context.OutputPath, destination: context.OutputPath);
            AddLanguage(args, FunctionsWorkerRuntime);
            AddLanguageVersion(args, FunctionsWorkerRuntime);
            AddBuildOptimizationFlags(args, context, Flags);
            AddWorkerRuntimeArgs(args, FunctionsWorkerRuntime);

            return args.ToString();
        }

        protected void AddOryxBuildCommand(StringBuilder args, DeploymentContext context, string source, string destination)
        {
            // If it is express build, we don't directly need to write to /home/site/wwwroot
            // So, we build into a different directory to avoid overlap
            // Additionally, we didn't run kudusync, and can just build directly from repository path
            if (Flags == BuildOptimizationsFlags.UseExpressBuild)
            {
                source = context.RepositoryPath;
                destination = OryxBuildConstants.FunctionAppBuildSettings.ExpressBuildSetup;
                // It is important to clean and recreate the directory to make sure no overwrite occurs
                if (FileSystemHelpers.DirectoryExists(destination))
                {
                    FileSystemHelpers.DeleteDirectorySafe(destination);
                }
                FileSystemHelpers.EnsureDirectory(destination);
            }
            OryxArgumentsHelper.AddOryxBuildCommand(args, source, destination);
        }

        protected void AddLanguage(StringBuilder args, WorkerRuntime workerRuntime)
        {
            switch (workerRuntime)
            {
                case WorkerRuntime.DotNet:
                    OryxArgumentsHelper.AddLanguage(args, "dotnet");
                    break;

                case WorkerRuntime.Node:
                    OryxArgumentsHelper.AddLanguage(args, "nodejs");
                    break;

                case WorkerRuntime.Python:
                    OryxArgumentsHelper.AddLanguage(args, "python");
                    break;

                case WorkerRuntime.PHP:
                    OryxArgumentsHelper.AddLanguage(args, "php");
                    break;
            }
        }

        protected void AddLanguageVersion(StringBuilder args, WorkerRuntime workerRuntime)
        {
            var workerVersion = ResolveWorkerRuntimeVersion(FunctionsWorkerRuntime);
            if (!string.IsNullOrEmpty(workerVersion))
            {
                OryxArgumentsHelper.AddLanguageVersion(args, workerVersion);
            }
        }

        protected void AddBuildOptimizationFlags(StringBuilder args, DeploymentContext context, BuildOptimizationsFlags optimizationFlags)
        {
            switch (Flags)
            {
                // By default, we always want to use the temp directory path
                // However, it's good to have an off option here.
                // Ideally, we would always use ExpressBuild, as that also performs run from package
                case BuildOptimizationsFlags.Off:
                    break;
                case BuildOptimizationsFlags.None:
                case BuildOptimizationsFlags.CompressModules:
                case BuildOptimizationsFlags.UseExpressBuild:
                    OryxArgumentsHelper.AddTempDirectoryOption(args, context.BuildTempPath);
                    break;
            }
        }

        protected void AddWorkerRuntimeArgs(StringBuilder args, WorkerRuntime workerRuntime)
        {
            switch (workerRuntime)
            {
                case WorkerRuntime.Python:
                    OryxArgumentsHelper.AddPythonPackageDir(args, OryxBuildConstants.FunctionAppBuildSettings.PythonPackagesTargetDir);
                    break;
            }
        }

        private WorkerRuntime ResolveWorkerRuntime()
        {
            var functionsWorkerRuntimeStr = GetEnvironmentVariableOrNull(OryxBuildConstants.FunctionAppEnvVars.WorkerRuntimeSetting);
            return FunctionAppSupportedWorkerRuntime.ParseWorkerRuntime(functionsWorkerRuntimeStr);
        }

        private string ResolveWorkerRuntimeVersion(WorkerRuntime workerRuntime)
        {
            return FunctionAppSupportedWorkerRuntime.GetDefaultLanguageVersion(workerRuntime);
        }

        private string GetEnvironmentVariableOrNull(string environmentVarName)
        {
            var environmentVarValue = System.Environment.GetEnvironmentVariable(environmentVarName);
            if (string.IsNullOrEmpty(environmentVarValue))
            {
                return null;
            }
            return environmentVarValue;
        }
    }
}
