using System.Threading.Tasks;

namespace Kudu.Core.LinuxConsumption
{
    public class NullMeshPersistentFileSystem : IMeshPersistentFileSystem
    {
        public Task<bool> MountFileShare()
        {
            return Task.FromResult(false);
        }

        public bool GetStatus(out string message)
        {
            message = string.Empty;
            return false;
        }

        public string GetDeploymentsPath()
        {
            return string.Empty;
        }
    }
}