namespace Kudu.Core.LinuxConsumption
{
    public interface ISystemEnvironment
    {
        string GetEnvironmentVariable(string name);

        void SetEnvironmentVariable(string name, string value);
    }
}
