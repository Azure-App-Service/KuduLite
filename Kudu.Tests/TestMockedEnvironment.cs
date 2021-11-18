using Kudu.Core;
using Microsoft.AspNetCore.Http;

namespace Kudu.Tests
{
    public class TestMockedEnvironment
    {
        public static IEnvironment GetMockedEnvironment(string rootPath = "rootPath", string binPath = "binPath", string repositoryPath = "repositoryPath", string requestId = "requestId", string kuduConsoleFullPath = "kuduConsoleFullPath", string appName = null)
        {
            if (appName != null)
            {
                rootPath = rootPath + "/" + appName;
            }

            return new Environment(rootPath, binPath, repositoryPath, requestId, kuduConsoleFullPath, new HttpContextAccessor(), appName);
        }
    }
}
