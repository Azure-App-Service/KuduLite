using System;
using Kudu.Contracts.Infrastructure;

namespace Kudu.Core.Deployment
{
    public interface IDeploymentStatusManager
    {
        IDeploymentStatusFile Create(string id, IEnvironment environment);
        IDeploymentStatusFile Open(string id, IEnvironment environment);
        void Delete(string id, IEnvironment environment);

        IOperationLock Lock { get; }

        string ActiveDeploymentId { get; set; }

        DateTime LastModifiedTime { get; }
    }
}
