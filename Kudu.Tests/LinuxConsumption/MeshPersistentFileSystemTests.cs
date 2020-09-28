using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kudu.Core.LinuxConsumption;
using Moq;
using Xunit;

namespace Kudu.Tests.LinuxConsumption
{
    [Collection("LinuxConsumption")]
    public class MeshPersistentFileSystemTests
    {
        private const string ConnectionString = "connection-string";

        private readonly TestSystemEnvironment _systemEnvironment;
        private readonly Mock<IMeshServiceClient> _client;
        private readonly Mock<IStorageClient> _storageClient;

        public MeshPersistentFileSystemTests()
        {
            var environmentVariables = new Dictionary<string, string>();
            environmentVariables[Constants.ContainerName] = "container-name";
            environmentVariables[Constants.EnablePersistentStorage] = "1";
            environmentVariables[Constants.AzureWebJobsStorage] = ConnectionString;

            _systemEnvironment = new TestSystemEnvironment(environmentVariables);

            _client = new Mock<IMeshServiceClient>(MockBehavior.Strict);
            _storageClient = new Mock<IStorageClient>(MockBehavior.Strict);

            _storageClient.Setup(s => s.CreateFileShare(It.IsAny<string>(), ConnectionString, It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            _client.Setup(c => c.MountCifs(ConnectionString, It.IsAny<string>(), Constants.KuduFileShareMountPath))
                .Returns(Task.CompletedTask);
        }

        [Fact]
        public async Task MountShareMounted()
        {
            var meshPersistentFileSystem =
                new MeshPersistentFileSystem(_systemEnvironment, _client.Object, _storageClient.Object);

            Assert.False(meshPersistentFileSystem.GetStatus(out var statusMessage));
            Assert.True(string.IsNullOrEmpty(statusMessage));

            var mountResult = await meshPersistentFileSystem.MountFileShare();
            Assert.True(mountResult);

            Assert.True(meshPersistentFileSystem.GetStatus(out statusMessage));
            Assert.True(string.IsNullOrEmpty(statusMessage));
            Assert.True(!string.IsNullOrEmpty(meshPersistentFileSystem.GetDeploymentsPath()));

            _storageClient.Verify(s => s.CreateFileShare(It.IsAny<string>(), ConnectionString, It.IsAny<string>()),
                Times.Once);
            _client.Verify(c => c.MountCifs(ConnectionString, It.IsAny<string>(), Constants.KuduFileShareMountPath),
                Times.Once);
        }

        [Fact]
        public async Task MountsOnlyOnce()
        {
            var meshPersistentFileSystem =
                new MeshPersistentFileSystem(_systemEnvironment, _client.Object, _storageClient.Object);

            Assert.False(meshPersistentFileSystem.GetStatus(out var statusMessage));
            Assert.True(string.IsNullOrEmpty(statusMessage));

            // Mount once
            var mountResult = await meshPersistentFileSystem.MountFileShare();
            Assert.True(mountResult);

            Assert.True(meshPersistentFileSystem.GetStatus(out statusMessage));
            Assert.True(string.IsNullOrEmpty(statusMessage));
            Assert.True(!string.IsNullOrEmpty(meshPersistentFileSystem.GetDeploymentsPath()));

            //Mount again
            mountResult = await meshPersistentFileSystem.MountFileShare();
            Assert.True(mountResult);
            Assert.True(meshPersistentFileSystem.GetStatus(out statusMessage));
            Assert.True(statusMessage.Contains("mounted already", StringComparison.Ordinal));
            Assert.True(!string.IsNullOrEmpty(meshPersistentFileSystem.GetDeploymentsPath()));

            // Assert share was mounted was called only once even if mount was called twice
            _storageClient.Verify(s => s.CreateFileShare(It.IsAny<string>(), ConnectionString, It.IsAny<string>()),
                Times.Once);
            _client.Verify(c => c.MountCifs(ConnectionString, It.IsAny<string>(), Constants.KuduFileShareMountPath),
                Times.Once);
        }

        [Fact]
        public async Task MountsOnLinuxConsumptionOnly()
        {
            // Container name will be null on non-Linux consumption environments
            _systemEnvironment.SetEnvironmentVariable(Constants.ContainerName, null);

            var meshPersistentFileSystem =
                new MeshPersistentFileSystem(_systemEnvironment, _client.Object, _storageClient.Object);

            Assert.False(meshPersistentFileSystem.GetStatus(out string _));

            var mountResult = await meshPersistentFileSystem.MountFileShare();
            Assert.False(mountResult);

            Assert.False(meshPersistentFileSystem.GetStatus(out var statusMessage));
            Assert.True(statusMessage.Contains("only supported on Linux consumption environment", StringComparison.Ordinal));

            Assert.True(string.IsNullOrEmpty(meshPersistentFileSystem.GetDeploymentsPath()));

            _storageClient.Verify(s => s.CreateFileShare(It.IsAny<string>(), ConnectionString, It.IsAny<string>()),
                Times.Never);
            _client.Verify(c => c.MountCifs(ConnectionString, It.IsAny<string>(), Constants.KuduFileShareMountPath),
                Times.Never);
        }


        [Fact]
        public async Task MountsOnlyIfPersistentStorageEnabled()
        {
            // Disable
            _systemEnvironment.SetEnvironmentVariable(Constants.EnablePersistentStorage, null);
            
            var meshPersistentFileSystem =
                new MeshPersistentFileSystem(_systemEnvironment, _client.Object, _storageClient.Object);

            Assert.False(meshPersistentFileSystem.GetStatus(out string _));

            var mountResult = await meshPersistentFileSystem.MountFileShare();
            Assert.False(mountResult);

            Assert.False(meshPersistentFileSystem.GetStatus(out var statusMessage));
            Assert.True(statusMessage.Contains("persistent storage is disabled", StringComparison.Ordinal));

            Assert.True(string.IsNullOrEmpty(meshPersistentFileSystem.GetDeploymentsPath()));

            _storageClient.Verify(s => s.CreateFileShare(It.IsAny<string>(), ConnectionString, It.IsAny<string>()),
                Times.Never);
            _client.Verify(c => c.MountCifs(ConnectionString, It.IsAny<string>(), Constants.KuduFileShareMountPath),
                Times.Never);
        }

        [Fact]
        public async Task MountsOnlyIfStorageAccountConfigured()
        {
            // Remove storage account
            _systemEnvironment.SetEnvironmentVariable(Constants.AzureWebJobsStorage, null);

            var meshPersistentFileSystem =
                new MeshPersistentFileSystem(_systemEnvironment, _client.Object, _storageClient.Object);

            Assert.False(meshPersistentFileSystem.GetStatus(out string _));

            var mountResult = await meshPersistentFileSystem.MountFileShare();
            Assert.False(mountResult);

            Assert.False(meshPersistentFileSystem.GetStatus(out var statusMessage));
            Assert.True(statusMessage.Contains($"{Constants.AzureWebJobsStorage} is empty", StringComparison.Ordinal));

            Assert.True(string.IsNullOrEmpty(meshPersistentFileSystem.GetDeploymentsPath()));

            _storageClient.Verify(s => s.CreateFileShare(It.IsAny<string>(), ConnectionString, It.IsAny<string>()),
                Times.Never);
            _client.Verify(c => c.MountCifs(ConnectionString, It.IsAny<string>(), Constants.KuduFileShareMountPath),
                Times.Never);
        }
    }
}
