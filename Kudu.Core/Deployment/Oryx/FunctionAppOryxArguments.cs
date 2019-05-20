using System.Text;

namespace Kudu.Core.Deployment.Oryx
{
    class FunctionAppOryxArguments : IOryxArguments
    {
        public bool RunOryxBuild { get; set; }

        public BuildOptimizationsFlags Flags { get; set; }

        private readonly WorkerRuntime FunctionsWorkerRuntime;
        public bool SkipKuduSync { get; set; }

        public FunctionAppOryxArguments()
        {
            SkipKuduSync = false;
            FunctionsWorkerRuntime = ResolveWorkerRuntime();
            RunOryxBuild = FunctionsWorkerRuntime != WorkerRuntime.None;
            var buildFlags = GetEnvironmentVariableOrNull(OryxBuildConstants.OryxEnvVars.BuildFlagsSetting);
            Flags = BuildFlagsHelper.Parse(buildFlags);
        }

        public string GenerateOryxBuildCommand(DeploymentContext context)
        {
            StringBuilder args = new StringBuilder();

            AddOryxBuildCommand(args, source: context.OutputPath, destination: context.OutputPath);
            AddLanguage(args, FunctionsWorkerRuntime);
            AddLanguageVersion(args, FunctionsWorkerRuntime);
            AddBuildOptimizationFlags(args, context, Flags);
            AddWorkerRuntimeArgs(args, FunctionsWorkerRuntime);

            return args.ToString();
        }

        private void AddOryxBuildCommand(StringBuilder args, string source, string destination)
        {
            OryxArgumentsHelper.AddOryxBuildCommand(args, source, destination);
        }

        private void AddLanguage(StringBuilder args, WorkerRuntime workerRuntime)
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
            }
        }

        private void AddLanguageVersion(StringBuilder args, WorkerRuntime workerRuntime)
        {
            var workerVersion = ResolveWorkerRuntimeVersion(FunctionsWorkerRuntime);
            if (!string.IsNullOrEmpty(workerVersion))
            {
                OryxArgumentsHelper.AddLanguageVersion(args, workerVersion);
            }
        }

        private void AddBuildOptimizationFlags(StringBuilder args, DeploymentContext context, BuildOptimizationsFlags optimizationFlags)
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

        private void AddWorkerRuntimeArgs(StringBuilder args, WorkerRuntime workerRuntime)
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
            // Note: FRAMEWORK is not set right now for a Function App. However, we are planning on doing so,
            // similar to what AppService does. Once we start setting that, this should be the order of preference
            var functionsWorkerRuntimeStr = GetEnvironmentVariableOrNull(OryxBuildConstants.OryxEnvVars.FrameworkSetting)
                ?? GetEnvironmentVariableOrNull(OryxBuildConstants.FunctionAppEnvVars.WorkerRuntimeSetting);

            return FunctionAppSupportedWorkerRuntime.ParseWorkerRuntime(functionsWorkerRuntimeStr);
        }

        private string ResolveWorkerRuntimeVersion(WorkerRuntime workerRuntime)
        {
            // Note: FRAMEWORK_VERSION is not set right now for a Function App. However, we are planning on doing so,
            // similar to what AppService does. Until then, we will always hit the defaults
            return GetEnvironmentVariableOrNull(OryxBuildConstants.OryxEnvVars.FrameworkVersionSetting)
                ?? FunctionAppSupportedWorkerRuntime.GetDefaultLanguageVersion(workerRuntime);
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
