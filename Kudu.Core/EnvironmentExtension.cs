using System.IO;

namespace Kudu.Core
{
    public static class EnvironmentExtension
    {
        public static string GetNormalizedK8SEAppName(this IEnvironment environment)
        {
            return environment?.K8SEAppName?.ToLowerInvariant();
        }

        public static string GetSettingsPath(this IEnvironment environment)
        {
            return Path.Combine(environment.DeploymentsPath, Constants.DeploySettingsPath);
        }
    }
}
