using System;
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
        
        public virtual bool IsHeld
        {
            get { return _lock.IsHeld; }
        }
        
        public LockFile(string path) 
        {
            _lock =  new NoOpLock();
            
        }

        public LockFile(string path, ITraceFactory traceFactory, bool ensureLock = false)
        {
            _lock = new NoOpLock();
      
        }
        
        
        public virtual bool Lock(string operationName)
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

        public virtual void Release()
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