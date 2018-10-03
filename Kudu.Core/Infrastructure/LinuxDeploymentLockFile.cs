using Kudu.Contracts.SourceControl;
using Kudu.Core.SourceControl;
using Kudu.Core.Tracing;

namespace Kudu.Core.Infrastructure
{
    public class LinuxDeploymentLockFile : LinuxLockFile
    {
        public LinuxDeploymentLockFile(string path) : base(path)
        {
        }

        public LinuxDeploymentLockFile(string path, ITraceFactory traceFactory, bool ensureLock = false) : base(path, traceFactory, ensureLock)
        {
        }
       
        public override void OnLockAcquired()
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