using System.IO;

namespace Kudu.Core.Deployment.Oryx
{
    internal static class OryxBuildConstants
    {
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
            public static readonly string Node = "10.14.1";
            public static readonly string Python = "3.6";
            public static readonly string Dotnet = "2.2";
        }

        internal static class FunctionAppBuildSettings
        {
            public static readonly string PythonPackagesTargetDir = Path.Combine(".python_packages", "lib", "python3.6", "site-packages");
        }
    }
}
