using Xunit;
using Kudu.Core.Helpers;

namespace Kudu.Tests.Core.Helpers
{
    [Collection("MockedEnvironmentVariablesCollection")]
    public class EnvironmentHelperTests
    {
        [Fact]
        public void OnLinuxConsumptionIfContainerNameIsSet()
        {
            using (new TestScopedEnvironmentVariable(Constants.ContainerName, "Container42"))
            {
                var result = EnvironmentHelper.IsOnLinuxConsumption();
                Assert.True(result);
            }
        }

        [Fact]
        public void NotOnLinuxConsumptionIfCoontainerNameAndInstanceIdAreSet()
        {
            using (new TestScopedEnvironmentVariable(Constants.ContainerName, "Container42"))
            using (new TestScopedEnvironmentVariable(Constants.AzureWebsiteInstanceId, "42"))
            {
                var result = EnvironmentHelper.IsOnLinuxConsumption();
                Assert.False(result);
            }
        }
    }
}
