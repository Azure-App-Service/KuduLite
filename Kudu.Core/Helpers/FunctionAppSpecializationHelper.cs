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
                toBeUpdatedEnv.Add(SettingsKeys.DoBuildDuringDeployment, "true");
                toBeUpdatedEnv.Add(OryxBuildConstants.EnableOryxBuild, "true");
            }
            return toBeUpdatedEnv;
        }
    }
}
