using System;
using System.Diagnostics.Tracing;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;

namespace Kudu.Core.LinuxConsumption
{
    public class FileSystemPathProvider : IFileSystemPathProvider
    {
        private readonly IMeshPersistentFileSystem _persistentFileSystem;

        public FileSystemPathProvider(IMeshPersistentFileSystem persistentFileSystem)
        {
            _persistentFileSystem =
                persistentFileSystem ?? throw new ArgumentNullException(nameof(persistentFileSystem));
        }

        public bool TryGetDeploymentsPath(out string path)
        {
            path = _persistentFileSystem.GetDeploymentsPath();
            return !string.IsNullOrEmpty(path) && EnsureMountedDeploymentsPath(path);
        }

        private bool EnsureMountedDeploymentsPath(string path)
        {
            try
            {
                FileSystemHelpers.EnsureDirectory(path);
                return true;
            }
            catch (Exception e)
            {
                KuduEventGenerator.Log().LogMessage(EventLevel.Informational, ServerConfiguration.GetApplicationName(),
                    $"{nameof(EnsureMountedDeploymentsPath)} Failed. Path = {path}", e.ToString());
                return false;
            }
        }
    }
}