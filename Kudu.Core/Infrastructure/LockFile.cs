using System.Threading.Tasks;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.SourceControl;
using Kudu.Core.Helpers;
using Kudu.Core.Tracing;

namespace Kudu.Core.Infrastructure
{
    public class LockFile : IOperationLock
    {        
        
        private IOperationLock _lock;

        public OperationLockInfo LockInfo
        {            
            get { return _lock.LockInfo;}
        }
        
        public bool IsHeld
        {
            get { return _lock.IsHeld; }
        }
        
        public LockFile(string path) 
        {
            if (!OSDetector.IsOnWindows())
            {
                _lock = new LinuxLockFile(path);
            }
            else
            {
                _lock =  new WindowsLockFile(path);
            }
        }

        public LockFile(string path, ITraceFactory traceFactory, bool ensureLock = false)
        {
            if (!OSDetector.IsOnWindows())
            {
                _lock = new LinuxLockFile(path,traceFactory,ensureLock);
            }
            else
            {
                _lock =  new WindowsLockFile(path,traceFactory,ensureLock);
            }
        }
        
        
        public bool Lock(string operationName)
        {
            return _lock.Lock(operationName);
        }

        public void InitializeAsyncLocks()
        {
             _lock.InitializeAsyncLocks();
        }

        public Task LockAsync(string operationName)
        {
            return  _lock.LockAsync(operationName);
        }

        public void Release()
        {
             _lock.Release();
        }

        public IRepositoryFactory RepositoryFactory { get; set; }

        public virtual void OnLockAcquired()
        {
            //no op
        }
    }
}