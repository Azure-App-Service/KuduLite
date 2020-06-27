using Kudu.Core;
using Kudu.Core.LinuxConsumption;
using Kudu.Tests.LinuxConsumption;
using Microsoft.AspNetCore.Http;

namespace Kudu.Tests
{
    public class TestMockedEnvironment
    {
        public static IEnvironment GetMockedEnvironment(string rootPath = "rootPath", string binPath = "binPath", string repositoryPath = "repositoryPath", string requestId = "requestId", string kuduConsoleFullPath = "kuduConsoleFullPath", IFileSystemPathProvider fileSystemPathProvider = null)
        {
            return new Environment(rootPath, binPath, repositoryPath, requestId, kuduConsoleFullPath, new HttpContextAccessor(), fileSystemPathProvider ?? TestFileSystemPathProvider.Instance);
        }
    }
}
