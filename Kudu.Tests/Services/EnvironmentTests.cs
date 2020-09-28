using System.IO.Abstractions;
using Kudu.Core;
using Kudu.Core.LinuxConsumption;
using Kudu.Tests.LinuxConsumption;
using Moq;
using Xunit;

namespace Kudu.Tests.Services
{
    [Collection("MockedEnvironmentVariablesCollection")]
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

        delegate void GetDeploymentPathCallback(out string path);     // needed for Callback
        delegate bool GetDeploymentPathReturn(out string path);      // needed for Returns

        [Fact]
        public void ReturnsDeploymentsPath()
        {
            const string somePath = "/some-path";

            var fileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var directory = new Mock<DirectoryBase>(MockBehavior.Strict);
            directory.Setup(d => d.Exists(It.IsAny<string>())).Returns(true);
            fileSystem.SetupGet(f => f.Directory).Returns(directory.Object);

            using (new MockFileSystem(fileSystem.Object))
            {
                var fileSystemPathProvider = new Mock<IFileSystemPathProvider>(MockBehavior.Strict);

                // Configure an override for deployments path
                fileSystemPathProvider.Setup(f => f.TryGetDeploymentsPath(out It.Ref<string>.IsAny)).
                    Callback(new GetDeploymentPathCallback((out string path) =>
                    {
                        path = somePath;
                    })).Returns(new GetDeploymentPathReturn((out string path) =>
                    {
                        path = somePath;
                        return true;
                    }));

                IEnvironment env = TestMockedEnvironment.GetMockedEnvironment(string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, fileSystemPathProvider.Object);
                Assert.Equal(somePath, env.DeploymentsPath);
                fileSystemPathProvider.Reset();

                // Return false from filesystempath provider so the default deployments path is returned
                fileSystemPathProvider.Setup(f => f.TryGetDeploymentsPath(out It.Ref<string>.IsAny)).
                    Callback(new GetDeploymentPathCallback((out string path) =>
                    {
                        path = somePath;
                    })).Returns(new GetDeploymentPathReturn((out string path) =>
                    {
                        path = somePath;
                        return false;
                    }));

                env = TestMockedEnvironment.GetMockedEnvironment(string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, fileSystemPathProvider.Object);
                Assert.NotEqual(somePath, env.DeploymentsPath);
                fileSystemPathProvider.Reset();

                // Uses default path when no override is set
                env = TestMockedEnvironment.GetMockedEnvironment();
                Assert.NotEqual(somePath, env.DeploymentsPath);
            }
        }
    }
}
