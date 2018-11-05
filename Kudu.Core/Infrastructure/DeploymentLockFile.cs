using System;
using System.Diagnostics;
using System.Threading;
using Kudu.Contracts.SourceControl;
using Kudu.Core.Helpers;
using Kudu.Core.SourceControl;
using Kudu.Core.Tracing;

namespace Kudu.Core.Infrastructure
{
    /// <summary>
    /// Specific to deployment lock.
    /// </summary>
    public class DeploymentLockFile : AllSafeLinuxLock
    {
        private static readonly ReaderWriterLockSlim rwl = new ReaderWriterLockSlim();
        private readonly string _path;
        private static DeploymentLockFile _deploymentLockFile;
        //private static AllSafeLinuxLock _linuxLock;

        public static DeploymentLockFile GetInstance(string path, ITraceFactory traceFactory)
        {
            if (_deploymentLockFile == null)
            {
                _deploymentLockFile = new DeploymentLockFile(path,traceFactory);
            }

            return _deploymentLockFile;
        }

        private DeploymentLockFile(string path, ITraceFactory traceFactory) : base(path,traceFactory)
        {
            _path = path;
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