using System;
using System.IO;
using Kudu.Contracts.Infrastructure;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using System.Collections.Generic;

namespace Kudu.Core.Deployment
{
    public class DeploymentStatusManager : IDeploymentStatusManager
    {
        public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
        private readonly IAnalytics _analytics;
        private readonly IOperationLock _statusLock;
        private readonly string _activeFile;

        public DeploymentStatusManager(IEnvironment environment,
                                       IAnalytics analytics,
                                       IDictionary<string, IOperationLock> namedLocks)
            : this(environment, analytics, namedLocks["status"])
        { }

        public DeploymentStatusManager(IEnvironment environment,
                                       IAnalytics analytics,
                                       IOperationLock statusLock)
        {
            _analytics = analytics;
            _statusLock = statusLock;
            _activeFile = Path.Combine(environment.DeploymentsPath, Constants.ActiveDeploymentFile);
        }

        public IDeploymentStatusFile Create(string id, IEnvironment environment)
        {
            return DeploymentStatusFile.Create(id, environment, _statusLock);
        }

        public IDeploymentStatusFile Open(string id, IEnvironment environment)
        {
            return DeploymentStatusFile.Open(id, environment, _analytics, _statusLock);
        }

        public void Delete(string id, IEnvironment environment)
        {
            string path = Path.Combine(environment.DeploymentsPath, id);

            _statusLock.LockOperation(() =>
            {
                FileSystemHelpers.DeleteDirectorySafe(path, ignoreErrors: true);

                // Used for ETAG
                if (FileSystemHelpers.FileExists(_activeFile))
                {
                    FileSystemHelpers.SetLastWriteTimeUtc(_activeFile, DateTime.UtcNow);
                }
                else
                {
                    FileSystemHelpers.WriteAllText(_activeFile, String.Empty);
                }
            }, "Deleting deployment", LockTimeout);
        }

        public IOperationLock Lock
        {
            get { return _statusLock; }
        }

        public string ActiveDeploymentId
        {
            get
            {
                return _statusLock.LockOperation(() =>
                {
                    if (FileSystemHelpers.FileExists(_activeFile))
                    {
                        return FileSystemHelpers.ReadAllText(_activeFile);
                    }

                    return null;
                }, "Getting active deployment id", LockTimeout);
            }
            set
            {
                _statusLock.LockOperation(() => FileSystemHelpers.WriteAllText(_activeFile, value), "Updating active deployment id", LockTimeout);
            }
        }


        public DateTime LastModifiedTime
        {
            get
            {
                return _statusLock.LockOperation(() =>
                {
                    if (FileSystemHelpers.FileExists(_activeFile))
                    {
                        return FileSystemHelpers.GetLastWriteTimeUtc(_activeFile);
                    }
                    else
                    {
                        return DateTime.MinValue;
                    }
                }, "Getting last deployment modified time", LockTimeout);
            }
        }
    }
}
