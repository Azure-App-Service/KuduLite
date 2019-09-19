using System;
using System.Collections.Generic;
using System.Text;
using Kudu.Core.Deployment.Oryx;
using LibGit2Sharp;

namespace Kudu.Core.Deployment
{
    public class AppServiceOryxArguments : IOryxArguments
    {
        public bool RunOryxBuild { get; set; }

        public BuildOptimizationsFlags Flags { get; set; }

        public Framework Language { get; set; }

        public string Version { get; set; }

        public string PublishFolder { get; set; }

        public string VirtualEnv { get; set; }

        public AppServiceOryxArguments()
        {
            RunOryxBuild = false;
            SkipKuduSync = false;

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
            else if (Language == Framework.DotNETCore)
            {
                // Skip kudu sync for .NET core builds
                SkipKuduSync = true;
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
                    // For python, enable compress option by default
                    if (Flags == BuildOptimizationsFlags.None)
                    {
                        Flags = BuildOptimizationsFlags.CompressModules;
                    }
                    return;

                case Framework.DotNETCore:
                    if (Flags == BuildOptimizationsFlags.None)
                    {
                        Flags = BuildOptimizationsFlags.UseTempDirectory;
                    }

                    return;

                case Framework.NodeJs:
                    // For node, enable compress option by default
                    if (Flags == BuildOptimizationsFlags.None)
                    {
                        Flags = BuildOptimizationsFlags.CompressModules;
                    }

                    return;

                case Framework.PHP:
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
            // Language
            switch (Language)
            {
                case Framework.None:
                    // Input/Output
                    OryxArgumentsHelper.AddOryxBuildCommand(args, source: context.OutputPath, destination: context.OutputPath);
                    break;

                case Framework.NodeJs:
                    // Input/Output
                    OryxArgumentsHelper.AddOryxBuildCommand(args, source: context.RepositoryPath, destination: context.RepositoryPath.Replace("repository","wwwroot"));
                    OryxArgumentsHelper.AddLanguage(args, "nodejs");
                    break;

                case Framework.Python:
                    // Input/Output
                    OryxArgumentsHelper.AddOryxBuildCommand(args, source: context.OutputPath, destination: context.OutputPath);
                    OryxArgumentsHelper.AddLanguage(args, "python");
                    break;

                case Framework.DotNETCore:
                    if (Flags == BuildOptimizationsFlags.UseExpressBuild)
                    {
                        // We don't want to copy the built artifacts to wwwroot for ExpressBuild scenario
                        OryxArgumentsHelper.AddOryxBuildCommand(args, source: context.RepositoryPath, destination: context.BuildTempPath);
                    }
                    else
                    {
                        // Input/Output [For .NET core, the source path is the RepositoryPath]
                        OryxArgumentsHelper.AddOryxBuildCommand(args, source: context.RepositoryPath, destination: context.OutputPath);
                    }
                    OryxArgumentsHelper.AddLanguage(args, "dotnet");
                    break;

                case Framework.PHP:
                    // Input/Output
                    OryxArgumentsHelper.AddOryxBuildCommand(args, source: context.OutputPath, destination: context.OutputPath);
                    OryxArgumentsHelper.AddLanguage(args, "php");
                    break;
            }

            // Version
            switch (Language)
            {
                case Framework.None:
                    break;
                case Framework.PHP:
                case Framework.NodeJs:
                    if (Version.Contains("LTS", StringComparison.OrdinalIgnoreCase))
                    {
                        // 10-LTS, 12-LTS should use versions 10, 12 etc
                        // Oryx Builder uses lts for major versions
                        Version = Version.Replace("LTS", "").Replace("-", "");
                        if (string.IsNullOrEmpty(Version))
                        {
                            // Current LTS
                            Version = "10";
                        }
                    }
                    break;
                case Framework.Python:
                    OryxArgumentsHelper.AddLanguageVersion(args, Version);
                    break;

                // work around issue regarding sdk version vs runtime version
                case Framework.DotNETCore:
                    if (Version == "1.0")
                    {
                        Version = "1.1";
                    }
                    else if (Version == "2.0")
                    {
                        Version = "2.1";
                    }

                    OryxArgumentsHelper.AddLanguageVersion(args, Version);
                    break;

                default:
                    break;
            }

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
                        OryxArgumentsHelper.AddPythonCompressOption(args, "tar-gz");
                    }

                    break;

                case BuildOptimizationsFlags.UseExpressBuild:
                    OryxArgumentsHelper.AddTempDirectoryOption(args, context.BuildTempPath);
                    if (Language == Framework.NodeJs)
                    {
                        OryxArgumentsHelper.AddNodeCompressOption(args, "zip");
                    }
                    else if (Language == Framework.Python)
                    {
                        OryxArgumentsHelper.AddPythonCompressOption(args, "zip");
                    }

                    break;

                case BuildOptimizationsFlags.UseTempDirectory:
                    OryxArgumentsHelper.AddTempDirectoryOption(args, context.BuildTempPath);
                    break;
            }

            // Virtual Env?
            if (!String.IsNullOrEmpty(VirtualEnv))
            {
                OryxArgumentsHelper.AddPythonVirtualEnv(args, VirtualEnv);
            }

            OryxArgumentsHelper.AddDebugLog(args);

            return args.ToString();
        }

        public bool SkipKuduSync { get; set; }

    }
}
