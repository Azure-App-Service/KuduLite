using Kudu.Contracts.SourceControl;
using Kudu.Core.SourceControl;
using Kudu.Core.Tracing;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Kudu.Core.Infrastructure
{
    /// <summary>
    /// Specific to deployment lock.
    /// </summary>
    public class DeploymentLockFile : AllSafeLinuxLock
    {
        private static readonly ReaderWriterLockSlim rwl = new ReaderWriterLockSlim();
        private static IDictionary<string, DeploymentLockFile> _deploymentLockFiles = new Dictionary<string, DeploymentLockFile>(StringComparer.OrdinalIgnoreCase);
        private static object lockObj = new object();

        public static DeploymentLockFile GetInstance(string path, ITraceFactory traceFactory)
        {
            DeploymentLockFile deploymentLockFile = new DeploymentLockFile(path, traceFactory);
            var key = deploymentLockFile.LocksPath;

            if (!_deploymentLockFiles.ContainsKey(key))
            {
                lock (lockObj)
                {
                    _deploymentLockFiles.Add(key, deploymentLockFile);
                }
            }

            return _deploymentLockFiles[key];
        }

        private DeploymentLockFile(string path, ITraceFactory traceFactory) : base(path, traceFactory)
        {
            /*
            if (!OSDetector.IsOnWindows())
            {
                _linuxLock = new LinuxLockFile(path+"_2",traceFactory);
                _isLinux = true;
            }
            */
        }

        public void OnLockAcquired()
        {
            IRepositoryFactory repositoryFactory = RepositoryFactory;
            if (repositoryFactory != null)
            {
                IRepository repository = repositoryFactory.GetRepository();
                if (repository != null)
                {
                    // Clear any left over repository-related lock since we have the actual lock
                    repository.ClearLock();
                }
            }
        }
    }
}