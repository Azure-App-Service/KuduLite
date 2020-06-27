using System;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading.Tasks;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;

namespace Kudu.Core.LinuxConsumption
{
    /// <summary>
    /// Provides persistent storage using Fileshares
    /// </summary>
    public class MeshPersistentFileSystem : IMeshPersistentFileSystem
    {
        private const string FileShareFormat = "{0}-{1}";

        private readonly ISystemEnvironment _environment;
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly IStorageClient _storageClient;

        private bool _fileShareMounted;
        private string _fileShareMountMessage;

        public MeshPersistentFileSystem(ISystemEnvironment environment, IMeshServiceClient meshServiceClient, IStorageClient storageClient)
        {
            _fileShareMounted = false;
            _fileShareMountMessage = string.Empty;
            _environment = environment;
            _meshServiceClient = meshServiceClient;
            _storageClient = storageClient;
        }

        private bool IsPersistentStorageEnabled()
        {
            var persistentStorageEnabled = _environment.GetEnvironmentVariable(Constants.EnablePersistentStorage);
            if (!string.IsNullOrWhiteSpace(persistentStorageEnabled))
            {
                return string.Equals("1", persistentStorageEnabled, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals("true", persistentStorageEnabled, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private bool TryGetStorageConnectionString(out string connectionString)
        {
            connectionString = _environment.GetEnvironmentVariable(Constants.AzureWebJobsStorage);
            return !string.IsNullOrWhiteSpace(connectionString);
        }

        private bool IsLinuxConsumption()
        {
            return !string.IsNullOrEmpty(_environment.GetEnvironmentVariable(Constants.ContainerName));
        }

        private bool IsKuduShareMounted()
        {
            return _fileShareMounted;
        }

        private void UpdateStatus(bool status, string message)
        {
            _fileShareMounted = status;
            _fileShareMountMessage = message;
        }

        /// <summary>
        /// Mounts file share
        /// </summary>
        /// <returns></returns>
        public async Task<bool> MountFileShare()
        {
            var siteName = ServerConfiguration.GetApplicationName();

            if (IsKuduShareMounted())
            {
                const string message = "Kudu file share mounted already";
                UpdateStatus(true, message);
                KuduEventGenerator.Log(_environment).LogMessage(EventLevel.Warning, siteName, nameof(MountFileShare), message);
                return true;
            }

            if (!IsLinuxConsumption())
            {
                const string message =
                    "Mounting kudu file share is only supported on Linux consumption environment";
                UpdateStatus(false, message);
                KuduEventGenerator.Log(_environment).LogMessage(EventLevel.Warning, siteName, nameof(MountFileShare), message);
                return false;
            }

            if (!IsPersistentStorageEnabled())
            {
                const string message = "Kudu file share was not mounted since persistent storage is disabled";
                UpdateStatus(false, message);
                KuduEventGenerator.Log(_environment).LogMessage(EventLevel.Warning, siteName, nameof(MountFileShare), message);
                return false;
            }

            if (!TryGetStorageConnectionString(out var connectionString))
            {
                var message = $"Kudu file share was not mounted since {Constants.AzureWebJobsStorage} is empty";
                UpdateStatus(false, message);
                KuduEventGenerator.Log(_environment).LogMessage(EventLevel.Warning, siteName, nameof(MountFileShare), message);
                return false;
            }

            var errorMessage = await MountKuduFileShare(siteName, connectionString);
            var mountResult = string.IsNullOrEmpty(errorMessage);

            UpdateStatus(mountResult, errorMessage);
            KuduEventGenerator.Log(_environment).LogMessage(EventLevel.Informational, siteName,
                $"Mounting Kudu file share result: {mountResult}", string.Empty);

            return mountResult;
        }

        public bool GetStatus(out string message)
        {
            message = _fileShareMountMessage;
            return _fileShareMounted;
        }

        public string GetDeploymentsPath()
        {
            if (_fileShareMounted)
            {
                return Path.Combine(Constants.KuduFileShareMountPath, "deployments");
            }

            return null;
        }

        private async Task<string> MountKuduFileShare(string siteName, string connectionString)
        {
            try
            {
                var fileShareName = string.Format(FileShareFormat, Constants.KuduFileSharePrefix,
                    ServerConfiguration.GetApplicationName().ToLowerInvariant());

                await _storageClient.CreateFileShare(siteName, connectionString, fileShareName);

                KuduEventGenerator.Log(_environment).LogMessage(EventLevel.Informational, siteName,
                    $"Mounting Kudu mount file share {fileShareName} at {Constants.KuduFileShareMountPath}",
                    string.Empty);

                await _meshServiceClient.MountCifs(connectionString, fileShareName, Constants.KuduFileShareMountPath);

                return string.Empty;
            }
            catch (Exception e)
            {
                var message = e.ToString();
                KuduEventGenerator.Log(_environment)
                    .LogMessage(EventLevel.Warning, siteName, nameof(MountKuduFileShare), message);
                return message;
            }
        }
    }
}
