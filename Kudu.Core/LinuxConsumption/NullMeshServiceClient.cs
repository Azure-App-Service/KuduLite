using System.Threading.Tasks;

namespace Kudu.Core.LinuxConsumption
{
    public class NullMeshServiceClient : IMeshServiceClient
    {
        public Task MountCifs(string connectionString, string contentShare, string targetPath)
        {
            return Task.CompletedTask;
        }
    }
}