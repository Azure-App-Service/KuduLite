using System.Threading.Tasks;

namespace Kudu.Core.LinuxConsumption
{
    public interface IStorageClient
    {
        Task CreateFileShare(string siteName, string connectionString, string fileShareName);
    }
}
