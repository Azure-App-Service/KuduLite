namespace Kudu.Core
{
    public static class EnvironmentExtension
    {
        public static string GetNormalizedK8SEAppName(this IEnvironment environment)
        {
            return environment?.K8SEAppName?.ToLowerInvariant();
        }
    }
}
