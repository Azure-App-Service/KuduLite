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
        public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
        private readonly IEnvironment _environment;
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
            _environment = environment;
            _analytics = analytics;
            _statusLock = statusLock;
            _activeFile = Path.Combine(environment.DeploymentsPath, Constants.ActiveDeploymentFile);
        }

        public IDeploymentStatusFile Create(string id)
        {
            Console.WriteLine($"IDeploymentStatusFile: Create");
            return DeploymentStatusFile.Create(id, _environment, _statusLock);
        }

        public IDeploymentStatusFile Open(string id)
        {
            Console.WriteLine($"IDeploymentStatusFile: Open");
            return DeploymentStatusFile.Open(id, _environment, _analytics, _statusLock);
        }

        public void Delete(string id)
        {
            string path = Path.Combine(_environment.DeploymentsPath, id);
            Console.WriteLine($"DeploymentStatusManager: Delete {id}, Before Lock");
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
            Console.WriteLine($"DeploymentStatusManager: Delete {id}, After Lock");
        }

        public IOperationLock Lock
        {
            get { return _statusLock; }
        }

        public string ActiveDeploymentId
        {
            get
            {
                Console.WriteLine($"DeploymentStatusManager: ActiveDeploymentId Get, Before Lock");
                var ret = _statusLock.LockOperation(() =>
                {
                    if (FileSystemHelpers.FileExists(_activeFile))
                    {
                        return FileSystemHelpers.ReadAllText(_activeFile);
                    }

                    return null;
                }, "Getting active deployment id", LockTimeout);
                Console.WriteLine($"DeploymentStatusManager: ActiveDeploymentId Get, After Lock");
                return ret;
            }
            set
            {
                Console.WriteLine($"DeploymentStatusManager: ActiveDeploymentId, Before Lock");
                _statusLock.LockOperation(() => FileSystemHelpers.WriteAllText(_activeFile, value), "Updating active deployment id", LockTimeout);
                Console.WriteLine($"DeploymentStatusManager: ActiveDeploymentId, After Lock");

            }
        }


        public DateTime LastModifiedTime
        {
            get
            {
                Console.WriteLine($"DeploymentStatusManager: LastModifiedTime, Before Lock");
                var ret = _statusLock.LockOperation(() =>
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
                Console.WriteLine($"DeploymentStatusManager: LastModifiedTime, After Lock");
                return ret;
            }
        }
    }
}
