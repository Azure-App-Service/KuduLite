using System;
using System.Diagnostics;
using System.Threading;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.SourceControl;
using Kudu.Core.Helpers;
using Kudu.Core.SourceControl;
using Kudu.Core.Tracing;

namespace Kudu.Core.Infrastructure
{
    /// <summary>
    /// Specific to deployment lock.
    /// </summary>
    public class DeploymentLockFile
    {
        private static readonly ReaderWriterLockSlim rwl = new ReaderWriterLockSlim();
        private readonly string _path;
        private static IOperationLock _deploymentLockFile;
        //private static AllSafeLinuxLock _linuxLock;

        public static IOperationLock GetInstance(string path, ITraceFactory traceFactory)
        {
            if (_deploymentLockFile == null)
            {
                _deploymentLockFile = new NoOpLock();
            }

            return _deploymentLockFile;
        }
       
    }
}