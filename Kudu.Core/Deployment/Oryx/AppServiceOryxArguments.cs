using System;
using System.Collections.Generic;
using System.Text;
using Kudu.Core.Deployment.Oryx;

namespace Kudu.Core.Deployment
{
    public class AppServiceOryxArguments : IOryxArguments
    {
        public bool RunOryxBuild { get; set; }

        public BuildOptimizationsFlags Flags { get; set; }

        private Framework Language { get; set; }

        private string Version { get; set; }

        private string PublishFolder { get; set; }

        private string VirtualEnv { get; set; }

        public AppServiceOryxArguments()
        {
            RunOryxBuild = false;

            string framework = System.Environment.GetEnvironmentVariable(OryxBuildConstants.OryxEnvVars.FrameworkSetting);
            string version = System.Environment.GetEnvironmentVariable(OryxBuildConstants.OryxEnvVars.FrameworkVersionSetting);
            string buildFlags = System.Environment.GetEnvironmentVariable(OryxBuildConstants.OryxEnvVars.BuildFlagsSetting);

            if (string.IsNullOrEmpty(framework) ||
                string.IsNullOrEmpty(version))
            {
                return;
            }

            Language = SupportedFrameworks.ParseLanguage(framework);
            if (Language == Framework.None)
            {
                return;
            }

            RunOryxBuild = true;
            Version = version;

            // Parse Build Flags
            Flags = BuildFlagsHelper.Parse(buildFlags);

            // Set language specific 
            SetLanguageOptions();
        }

        private void SetLanguageOptions()
        {
            switch(Language)
            {
                case Framework.None:
                    return;

                case Framework.Python:
                    SetVirtualEnvironment();
                    return;

                case Framework.DotNETCore:
                    return;

                case Framework.NodeJs:
                    // For node, enable compress option by default
                    if (Flags == BuildOptimizationsFlags.None)
                    {
                        Flags = BuildOptimizationsFlags.CompressModules;
                    }

                    return;
            }
        }

        private void SetVirtualEnvironment()
        {
            string virtualEnvName = "antenv";
            if (Version.StartsWith("3.6"))
            {
                virtualEnvName = "antenv3.6";
            }
            else if (Version.StartsWith("2.7"))
            {
                virtualEnvName = "antenv2.7";
            }

            VirtualEnv = virtualEnvName;
        }

        public string GenerateOryxBuildCommand(DeploymentContext context)
        {
            StringBuilder args = new StringBuilder();

            // Input/Output
            OryxArgumentsHelper.AddOryxBuildCommand(args, source: context.OutputPath, destination: context.OutputPath);

            // Language
            switch (Language)
            {
                case Framework.None:
                    break;

                case Framework.NodeJs:
                    OryxArgumentsHelper.AddLanguage(args, "nodejs");
                    break;

                case Framework.Python:
                    OryxArgumentsHelper.AddLanguage(args, "python");
                    break;

                case Framework.DotNETCore:
                    OryxArgumentsHelper.AddLanguage(args, "dotnet");
                    break;
            }

            // Version
            OryxArgumentsHelper.AddLanguageVersion(args, Version);

            // Build Flags
            switch (Flags)
            {
                case BuildOptimizationsFlags.Off:
                case BuildOptimizationsFlags.None:
                    break;

                case BuildOptimizationsFlags.CompressModules:
                    OryxArgumentsHelper.AddTempDirectoryOption(args, context.BuildTempPath);
                    if (Language == Framework.NodeJs)
                    {
                        OryxArgumentsHelper.AddNodeCompressOption(args, "tar-gz");
                    }
                    else if (Language == Framework.Python)
                    {
                        OryxArgumentsHelper.AddPythonCompressOption(args);
                    }

                    break;

                case BuildOptimizationsFlags.UseExpressBuild:
                    OryxArgumentsHelper.AddTempDirectoryOption(args, context.BuildTempPath);
                    if (Language == Framework.NodeJs)
                    {
                        OryxArgumentsHelper.AddNodeCompressOption(args, "zip");
                    }

                    break;
            }

            // Virtual Env?
            if (!String.IsNullOrEmpty(VirtualEnv))
            {
                OryxArgumentsHelper.AddPythonVirtualEnv(args, VirtualEnv);
            }

            // Publish Output?
            if (!String.IsNullOrEmpty(PublishFolder))
            {
                OryxArgumentsHelper.AddPublishedOutputPath(args, PublishFolder);
            }

            return args.ToString();
        }
    }
}
