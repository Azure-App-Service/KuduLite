using Kudu.Core.Tracing;
using Kudu.Tests.LinuxConsumption;
using Xunit;

namespace Kudu.Tests.Services
{
    [Collection("MockedEnvironmentVariablesCollection")]
    public class KuduEventGeneratorTests
    {
        [Fact]
        public void ReturnsLinuxEventGenerator()
        {
            var environment = new TestSystemEnvironment();
            environment.SetEnvironmentVariable(Constants.ContainerName, "container-name");

            var eventGenerator = KuduEventGenerator.Log(environment);
            Assert.True(eventGenerator is LinuxContainerEventGenerator);
        }
    }
}
