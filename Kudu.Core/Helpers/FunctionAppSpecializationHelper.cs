using Kudu.Contracts.Settings;
using Kudu.Core.Deployment.Oryx;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Helpers
{
    /// <summary>
    /// Handle Function App container specialization when running in Service Fabric Mesh
    /// </summary>
    public static class FunctionAppSpecializationHelper
    {
        /// <summary>
        /// According to the existing environment variables key-value pair, change the container environment.
        /// </summary>
        /// <param name="envKey">The key of environment variable</param>
        /// <param name="envValue">The value of environment variable</param>
        /// <returns>The new environment variables should be set</returns>
        public static Dictionary<string, string> HandleLinuxConsumption(string envKey, string envValue)
        {
            Dictionary<string, string> toBeUpdatedEnv = new Dictionary<string, string>();
            if (envKey == OryxBuildConstants.FunctionAppEnvVars.WorkerRuntimeSetting)
            {
                HandleFunctionWorkerRuntime(toBeUpdatedEnv, envValue);
            }
            return toBeUpdatedEnv;
        }

        private static void HandleFunctionWorkerRuntime(IDictionary<string, string> result, string workerRuntime)
        {
            if (workerRuntime == "python")
            {
                result.Add(SettingsKeys.DoBuildDuringDeployment, "true");
                result.Add(OryxBuildConstants.EnableOryxBuild, "true");
                result.Add(OryxBuildConstants.OryxEnvVars.FrameworkSetting, "PYTHON");
                result.Add(OryxBuildConstants.OryxEnvVars.FrameworkVersionSetting, "3.6");
            }
        }
    }
}
