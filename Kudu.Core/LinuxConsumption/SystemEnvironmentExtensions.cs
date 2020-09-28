namespace Kudu.Core.LinuxConsumption
{
    public static class SystemEnvironmentExtensions
    {
        public static bool IsOnLinuxConsumption(this ISystemEnvironment environment)
        {
            return !string.IsNullOrEmpty(environment.GetEnvironmentVariable(Constants.ContainerName));
        }
    }
}
