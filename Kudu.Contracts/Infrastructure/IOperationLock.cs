using System;
using System.Threading.Tasks;
using Kudu.Contracts.SourceControl;

namespace Kudu.Contracts.Infrastructure
{
    public interface IOperationLock
    {
        bool IsHeld { get; }
        OperationLockInfo LockInfo { get; }
        bool Lock(string operationName);
        void InitializeAsyncLocks();

        // Waits until lock can be acquired after which the task completes.
        Task LockAsync(string operationName);
        void Release();

    }
}
