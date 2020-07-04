using System.Threading.Tasks;

namespace Kudu.Core.LinuxConsumption
{
    public interface IMeshServiceClient
    {
        Task MountCifs(string connectionString, string contentShare, string targetPath);
    }
}
