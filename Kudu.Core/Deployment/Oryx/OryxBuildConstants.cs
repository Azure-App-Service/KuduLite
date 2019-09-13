using System.IO;

namespace Kudu.Core.Deployment.Oryx
{
    internal static class OryxBuildConstants
    {
        internal static readonly string EnableOryxBuild = "ENABLE_ORYX_BUILD";

        internal static class OryxEnvVars
        {
            public static readonly string FrameworkSetting = "FRAMEWORK";
            public static readonly string FrameworkVersionSetting = "FRAMEWORK_VERSION";
            public static readonly string BuildFlagsSetting = "BUILD_FLAGS";
        }

        internal static class FunctionAppEnvVars
        {
            public static readonly string WorkerRuntimeSetting = "FUNCTIONS_WORKER_RUNTIME";
        }

        internal static class FunctionAppWorkerRuntimeDefaults
        {
            public static readonly string Node = "8.15";
            public static readonly string Python = "3.6";
            public static readonly string Dotnet = "2.2";
            public static readonly string PHP = "7.3";
        }

        internal static class FunctionAppBuildSettings
        {
            public static readonly string ExpressBuildSetup = "/tmp/build/expressbuild";
            public static readonly string LinuxConsumptionArtifactName = "functionappartifact.squashfs";
            public static readonly string PythonPackagesTargetDir = Path.Combine(".python_packages", "lib", "python3.6", "site-packages");

            // Determine how many built files should be kept in the container
            public static readonly int ExpressBuildMaxFiles = 3;
            public static readonly int ConsumptionBuildMaxFiles = 1;
        }
    }
}
