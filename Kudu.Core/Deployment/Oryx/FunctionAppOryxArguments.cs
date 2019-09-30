using Kudu.Core.Infrastructure;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Kudu.Core.Deployment.Oryx
{
    public class FunctionAppOryxArguments : IOryxArguments
    {
        public bool RunOryxBuild { get; set; }

        public BuildOptimizationsFlags Flags { get; set; }

        protected readonly WorkerRuntime FunctionsWorkerRuntime;
        public bool SkipKuduSync { get; set; }
        public string Version { get; set; }
        public Framework Language { get; set; }
        public string PublishFolder { get; set; }
        public string VirtualEnv { get; set; }

        public FunctionAppOryxArguments()
        {
            SkipKuduSync = false;
            FunctionsWorkerRuntime = ResolveWorkerRuntime();
            RunOryxBuild = FunctionsWorkerRuntime != WorkerRuntime.None;
            var buildFlags = GetEnvironmentVariableOrNull(OryxBuildConstants.OryxEnvVars.BuildFlagsSetting);
            Flags = BuildFlagsHelper.Parse(buildFlags, defaultVal: BuildOptimizationsFlags.UseExpressBuild);
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
                    Language = Framework.DotNETCore;
                    OryxArgumentsHelper.AddLanguage(args, "dotnet");
                    break;

                case WorkerRuntime.Node:
                    Language = Framework.NodeJs;
                    OryxArgumentsHelper.AddLanguage(args, "nodejs");
                    break;

                case WorkerRuntime.Python:
                    Language = Framework.Python;
                    OryxArgumentsHelper.AddLanguage(args, "python");
                    break;
            }
        }

        protected void AddLanguageVersion(StringBuilder args, WorkerRuntime workerRuntime)
        {
            var workerVersion = ResolveWorkerRuntimeVersion(FunctionsWorkerRuntime);
            if (!string.IsNullOrEmpty(workerVersion))
            {
                Version = workerVersion;
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
                    if (Version == "3.6")
                    {
                        // Backward compatible with current python 3.6
                        OryxArgumentsHelper.AddPythonPackageDir(args, OryxBuildConstants.FunctionAppBuildSettings.Python36PackagesTargetDir);
                    }
                    else
                    {
                        OryxArgumentsHelper.AddPythonPackageDir(args, OryxBuildConstants.FunctionAppBuildSettings.PythonPackagesTargetDir);
                    }
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
            var framework = GetEnvironmentVariableOrNull(OryxBuildConstants.OryxEnvVars.FrameworkSetting);
            var frameworkVersion = GetEnvironmentVariableOrNull(OryxBuildConstants.OryxEnvVars.FrameworkVersionSetting);

            // If either of them is not set we are not in a position to determine the version, and should let the caller
            // switch to default
            if (string.IsNullOrEmpty(framework) || string.IsNullOrEmpty(frameworkVersion))
            {
                return FunctionAppSupportedWorkerRuntime.GetDefaultLanguageVersion(workerRuntime);
            }

            // If it's set to DOCKER, it could be a) a function app image b) a custom image
            // For custom image, there's no point doing a build, so we just default them (the parser won't work).
            // if it's indeed a function app image, we look for the right tag that tells us about the version.
            //
            // Or if it's set to a supported worker runtime that was inferred, we assume that the framework version
            // that is set is the correct version requested.
            if (framework.Equals("DOCKER", StringComparison.OrdinalIgnoreCase))
            {
                var parsedVersion = ParseRuntimeVersionFromImage(frameworkVersion);
                if (!string.IsNullOrEmpty(parsedVersion))
                {
                    return parsedVersion;
                }
            }
            else if (framework.Equals(workerRuntime.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return frameworkVersion;
            }

            return FunctionAppSupportedWorkerRuntime.GetDefaultLanguageVersion(workerRuntime);
        }

        public static string ParseRuntimeVersionFromImage(string imageName)
        {
            // The image name would be in this format -- 'mcr.microsoft.com/azure-functions/python:2.0-python3.6-appservice'
            // If we get any parsing issues, we return null here.
            try
            {
                var imageTag = imageName.Substring(imageName.LastIndexOf(':') + 1);
                var versionRegex = new Regex(@"^[0-9]+\.[0-9]+\-[A-Za-z]+([0-9].*)\-appservice$");
                var versionMatch = versionRegex.Match(imageTag);
                var version = versionMatch?.Groups[1]?.Value;
                if (!string.IsNullOrEmpty(version))
                {
                    return version;
                }
                return null;
            }
            catch (Exception)
            {
                // TODO: Log at ETW event later
                return null;
            }
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
