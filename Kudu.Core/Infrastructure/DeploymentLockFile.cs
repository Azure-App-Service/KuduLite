using System;
using Kudu.Contracts.SourceControl;
using Kudu.Core.SourceControl;
using Kudu.Core.Tracing;

namespace Kudu.Core.Infrastructure
{
    /// <summary>
    /// Specific to deployment lock.
    /// </summary>
    public class DeploymentLockFile : LockFile
    {
        public DeploymentLockFile(string path, ITraceFactory traceFactory)
            : base(path, traceFactory)
        {
        }

        override public void OnLockAcquired()
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