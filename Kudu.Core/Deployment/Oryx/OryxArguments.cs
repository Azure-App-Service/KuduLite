using System;
using System.Collections.Generic;
using System.Text;
using Kudu.Core.Deployment.Oryx;

namespace Kudu.Core.Deployment
{
    public class OryxArguments
    {
        public bool RunOryxBuild { get; set; }

        public Framework Language { get; set; }

        public string Version { get; set; }

        public string PublishFolder { get; set; }

        public string VirtualEnv { get; private set; }

        public BuildOptimizationsFlags Flags { get; set; }

        public OryxArguments()
        {
            RunOryxBuild = false;

            string framework = System.Environment.GetEnvironmentVariable("FRAMEWORK");
            string version = System.Environment.GetEnvironmentVariable("FRAMEWORK_VERSION");
            string buildFlags = System.Environment.GetEnvironmentVariable("BUILD_FLAGS");

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

        public string GenerateOryxBuildCommand(DeploymentContext context, string repositoryPath)
        {
            StringBuilder args = new StringBuilder();

            // Input/Output
            args.AppendFormat("oryx build {0} -o {1}", repositoryPath, context.OutputPath);

            // Language
            switch (Language)
            {
                case Framework.None:
                    break;

                case Framework.NodeJs:
                    args.AppendFormat(" -l nodejs");
                    break;

                case Framework.Python:
                    args.AppendFormat(" -l python");
                    break;

                case Framework.DotNETCore:
                    args.AppendFormat(" -l dotnet");
                    break;
            }

            // Version
            args.AppendFormat(" --language-version {0}", Version);

            // Build Flags
            switch (Flags)
            {
                case BuildOptimizationsFlags.Off:
                case BuildOptimizationsFlags.None:
                    break;

                case BuildOptimizationsFlags.CompressModules:
                    AddTempDirectoryOption(args, context.BuildTempPath);
                    if (Language == Framework.NodeJs)
                    {
                        AddNodeCompressOption(args, "tar-gz");
                    }
                    else if (Language == Framework.Python)
                    {
                        AddPythonCompressOption(args);
                    }

                    break;

                case BuildOptimizationsFlags.UseExpressBuild:
                    AddTempDirectoryOption(args, context.BuildTempPath);
                    if (Language == Framework.NodeJs)
                    {
                        AddNodeCompressOption(args, "zip");
                    }

                    break;
            }

            // Virtual Env?
            if (!String.IsNullOrEmpty(VirtualEnv))
            {
                args.AppendFormat(" -p {0}", VirtualEnv);
            }

            // Publish Output?
            if (!String.IsNullOrEmpty(PublishFolder))
            {
                args.AppendFormat(" -publishedOutputPath {0}", PublishFolder);
            }

            return args.ToString();
        }

        private static void AddTempDirectoryOption(StringBuilder args, string tempDir)
        {
            args.AppendFormat(" -i {0}", tempDir);
        }

        private static void AddNodeCompressOption(StringBuilder args, string format)
        {
            args.AppendFormat(" -p compress_node_modules={0}", format);
        }

        private static void AddPythonCompressOption(StringBuilder args)
        {
            args.AppendFormat(" -p zip_venv_dir");
        }
    }
}
