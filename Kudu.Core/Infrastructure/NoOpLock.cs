using Kudu.Contracts.Infrastructure;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Kudu.Core.Infrastructure
{
    public class NoOpLock : IOperationLock
    {
        public bool IsHeld => false;

        public OperationLockInfo LockInfo => new OperationLockInfo();

        public void InitializeAsyncLocks()
        {
            //
        }

        public bool Lock(string operationName)
        {
            return true;
        }

        public Task LockAsync(string operationName)
        {
            return Task.Run(() => true);
        }

        public void Release()
        {
            //
        }
    }
}
