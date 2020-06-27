using System.IO;
using System.IO.Abstractions;
using Kudu.Core.LinuxConsumption;
using Moq;
using Xunit;

namespace Kudu.Tests.LinuxConsumption
{
    [Collection("LinuxConsumption")]
    public class FileSystemPathProviderTests
    {
        [Fact]
        public void ReturnsDeploymentsPath()
        {
            const string deploymentsPath = "/path-0";

            var mockFileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var directory = new Mock<DirectoryBase>(MockBehavior.Strict);
            directory.Setup(d => d.Exists(deploymentsPath)).Returns(true);
            mockFileSystem.SetupGet(f => f.Directory).Returns(directory.Object);
            
            using (new MockFileSystem(mockFileSystem.Object))
            {
                var fileSystem = new Mock<IMeshPersistentFileSystem>(MockBehavior.Strict);
                const string expectedDeploymentsPath = deploymentsPath;
                fileSystem.Setup(f => f.GetDeploymentsPath()).Returns(expectedDeploymentsPath);

                var fileSystemPathProvider = new FileSystemPathProvider(fileSystem.Object);
                Assert.True(fileSystemPathProvider.TryGetDeploymentsPath(out string actualDeploymentsPath));
                Assert.Equal(expectedDeploymentsPath, actualDeploymentsPath);
            }
        }

        [Fact]
        public void ReturnsEmptyPathWhenDeploymentDirectoryCreationFails()
        {
            const string deploymentsPath = "/path-0";

            var mockFileSystem = new Mock<IFileSystem>(MockBehavior.Strict);
            var directory = new Mock<DirectoryBase>(MockBehavior.Strict);
            directory.Setup(d => d.Exists(deploymentsPath)).Returns(false);
            directory.Setup(d => d.CreateDirectory(deploymentsPath)).Throws(new IOException());
            mockFileSystem.SetupGet(f => f.Directory).Returns(directory.Object);

            using (new MockFileSystem(mockFileSystem.Object))
            {
                var fileSystem = new Mock<IMeshPersistentFileSystem>(MockBehavior.Strict);
                fileSystem.Setup(f => f.GetDeploymentsPath()).Returns(deploymentsPath);

                var fileSystemPathProvider = new FileSystemPathProvider(fileSystem.Object);
                Assert.False(fileSystemPathProvider.TryGetDeploymentsPath(out string actualDeploymentsPath));
                Assert.Equal(deploymentsPath, actualDeploymentsPath);
            }
        }

        [Fact]
        public void ReturnsEmptyWhenNoDeploymentsPathConfigured()
        {
            var fileSystem = new Mock<IMeshPersistentFileSystem>(MockBehavior.Strict);
            fileSystem.Setup(f => f.GetDeploymentsPath()).Returns(string.Empty);

            var fileSystemPathProvider = new FileSystemPathProvider(fileSystem.Object);
            Assert.False(fileSystemPathProvider.TryGetDeploymentsPath(out string actualDeploymentsPath));
            Assert.Equal(string.Empty, actualDeploymentsPath);
        }
    }
}
