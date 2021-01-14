using System.Threading.Tasks;

namespace Kudu.Core.LinuxConsumption
{
    public interface IMeshPersistentFileSystem
    {
        Task<bool> MountFileShare();

        bool GetStatus(out string message);

        string GetDeploymentsPath();
    }
}