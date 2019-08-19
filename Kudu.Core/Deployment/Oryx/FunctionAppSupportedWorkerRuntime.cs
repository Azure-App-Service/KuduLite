using System;

namespace Kudu.Core.Deployment.Oryx
{
    public enum WorkerRuntime
    {
        None,
        Node,
        Python,
        DotNet,
        PHP
    }

    public class FunctionAppSupportedWorkerRuntime
    {
        public static WorkerRuntime ParseWorkerRuntime(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return WorkerRuntime.None;
            }
            else if (value.StartsWith("NODE", StringComparison.OrdinalIgnoreCase))
            {
                return WorkerRuntime.Node;
            }
            else if (value.StartsWith("PYTHON", StringComparison.OrdinalIgnoreCase))
            {
                return WorkerRuntime.Python;
            }
            else if (value.StartsWith("DOTNET", StringComparison.OrdinalIgnoreCase))
            {
                return WorkerRuntime.DotNet;
            }
            else if (value.StartsWith("PHP", StringComparison.OrdinalIgnoreCase))
            {
                return WorkerRuntime.PHP;
            }

            return WorkerRuntime.None;
        }

        public static string GetDefaultLanguageVersion(WorkerRuntime workerRuntime)
        {
            switch (workerRuntime)
            {
                case WorkerRuntime.DotNet:
                    return OryxBuildConstants.FunctionAppWorkerRuntimeDefaults.Dotnet;

                case WorkerRuntime.Node:
                    return OryxBuildConstants.FunctionAppWorkerRuntimeDefaults.Node;

                case WorkerRuntime.Python:
                    return OryxBuildConstants.FunctionAppWorkerRuntimeDefaults.Python;

                case WorkerRuntime.PHP:
                    return OryxBuildConstants.FunctionAppWorkerRuntimeDefaults.PHP;

                default:
                    return "";
            }
        }
    }
}
