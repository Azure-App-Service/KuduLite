using Kudu.Core;
using Kudu.Services;
using Kudu.Tests;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kudu.Tests.Services
{
    public class EnvironmentTests
    {
        [Fact]
        public void LinuxConsumptionPlan()
        {
            using(new TestScopedEnvironmentVariable("CONTAINER_NAME", "cn"))
            {
                IEnvironment env = TestMockedEnvironment.GetMockedEnvironment();
                Assert.True(env.IsOnLinuxConsumption);
            }
        }

        [Fact]
        public void LinuxDedicatedPlan()
        {
            using (new TestScopedEnvironmentVariable("WEBSITE_INSTANCE_ID", "wii"))
            {
                IEnvironment env = TestMockedEnvironment.GetMockedEnvironment();
                Assert.False(env.IsOnLinuxConsumption);
            }
        }

        [Fact]
        public void InvalidLinuxPlan()
        {
            using (new TestScopedEnvironmentVariable("CONTAINER_NAME", "cn"))
            using (new TestScopedEnvironmentVariable("WEBSITE_INSTANCE_ID", "wii"))
            {
                IEnvironment env = TestMockedEnvironment.GetMockedEnvironment();
                Assert.False(env.IsOnLinuxConsumption);
            }
        }

        [Fact]
        public void UnsetLinuxPlan()
        {
            IEnvironment env = TestMockedEnvironment.GetMockedEnvironment();
            Assert.False(env.IsOnLinuxConsumption);
        }
    }
}
