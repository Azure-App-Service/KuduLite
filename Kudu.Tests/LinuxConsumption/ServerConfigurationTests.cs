using Kudu.Core.Infrastructure;
using Xunit;

namespace Kudu.Tests.LinuxConsumption
{
    [Collection("MockedEnvironmentVariablesCollection")]
    public class ServerConfigurationTests
    {
        [Fact]
        public void ReturnsApplicationName()
        {
            var testSystemEnvironment = new TestSystemEnvironment();
            const string placeholderSiteName = "placeholder-site-name";
            testSystemEnvironment.SetEnvironmentVariable(Constants.ContainerName, "c1"); // linux consumption
            testSystemEnvironment.SetEnvironmentVariable(Constants.WebsiteSiteName, placeholderSiteName);
            var serverConfiguration = new ServerConfiguration(testSystemEnvironment);
            Assert.Equal(placeholderSiteName, serverConfiguration.ApplicationName);

            // Updating the env variable should reflect immediately. since sitename is not cached
            const string testSite2 = "test-site-2";
            testSystemEnvironment.SetEnvironmentVariable(Constants.WebsiteSiteName, testSite2);
            Assert.Equal(testSite2, serverConfiguration.ApplicationName);
        }
    }
}
