namespace Kudu.Core.LinuxConsumption
{
    public interface IFileSystemPathProvider
    {
        bool TryGetDeploymentsPath(out string path);
    }
}
