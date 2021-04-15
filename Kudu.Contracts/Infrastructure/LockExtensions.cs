using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Contracts.Infrastructure
{
    public static class LockExtensions
    {
        private static readonly TimeSpan _sleepInterval = TimeSpan.FromMilliseconds(250);

        // try acquire lock and then execute the operation
        // return true if lock acquired and operation executed
        public static bool TryLockOperation(this IOperationLock lockObj,
                                         Action operation,
                                         string operationName,
                                         TimeSpan timeout)
        {
            var elapsed = TimeSpan.Zero;
            var ignoreMsg = false;

            if(operationName.Contains("status"))
            {
                ignoreMsg = true;
            }

            if (!ignoreMsg)
                Console.WriteLine("trying to acquire lock:" + operationName + ":" + DateTime.UtcNow.ToString() + "on lockObj:" + lockObj);

            while (!lockObj.Lock(operationName))
            {
                if (elapsed >= timeout)
                {
                    if (!ignoreMsg)
                        Console.WriteLine(" ######### tryLock = false on lockObj:" + lockObj +",op=" + operation);
                    return false;
                }

              //  Console.WriteLine("trying to acquire lock, retrying after sleeping for " + timeout.TotalMilliseconds + " ms");
                Thread.Sleep(_sleepInterval);
                elapsed += _sleepInterval;
            }
            
            try
            {
                //Console.WriteLine("tryLockOp got lock, now performing the task");
                operation();
                //Console.WriteLine("trying to acquire lock returning true after completing the task");
                return true;
            }
            finally
            {
                if (!ignoreMsg)
                    Console.WriteLine("releasing lock:" + operationName + ":" + DateTime.UtcNow.ToString() + "on lockObj:" + lockObj);

                lockObj.Release();
            }
        }

        // acquire lock and then execute the operation
        public static void LockOperation(this IOperationLock lockObj, Action operation, string operationName, TimeSpan timeout)
        {
            bool success = lockObj.TryLockOperation(operation, operationName, timeout);

            if (!success)
            {
                var lockInfo = lockObj.LockInfo;
                throw new LockOperationException(String.Format(CultureInfo.CurrentCulture, Resources.Error_OperationLockTimeout, operationName, lockInfo.OperationName, lockInfo.AcquiredDateTime));
            }
        }

        // acquire lock and then execute the operation
        public static T LockOperation<T>(this IOperationLock lockObj, Func<T> operation, string operationName, TimeSpan timeout)
        {
            T result = default(T);

            if(!operationName.Contains("status"))
                Console.WriteLine("acquiring lock on " + lockObj +", operationName=" + operationName);
            
            bool success = lockObj.TryLockOperation(() => result = operation(), operationName, timeout);

            if (!success)
            {
                var lockInfo = lockObj.LockInfo;
                throw new LockOperationException(String.Format(CultureInfo.CurrentCulture, Resources.Error_OperationLockTimeout, operationName, operationName,""));
            }
            
            return result;
        }

        public static async Task LockOperationAsync(this IOperationLock lockObj, Func<Task> operation, string operationName, TimeSpan timeout)
        {
            bool isLocked = await WaitToLockAsync(lockObj, operationName, timeout);
            if (!isLocked)
            {
                var lockInfo = lockObj.LockInfo;
                throw new LockOperationException(String.Format(CultureInfo.CurrentCulture, Resources.Error_OperationLockTimeout, operationName, lockInfo.OperationName, lockInfo.AcquiredDateTime));
            }

            try
            {
                await operation();
            }
            finally
            {
                lockObj.Release();
            }
        }

        public static async Task<T> LockOperationAsync<T>(this IOperationLock lockObj, Func<Task<T>> operation, string operationName, TimeSpan timeout)
        {
            bool isLocked = await WaitToLockAsync(lockObj, operationName, timeout);
            if (!isLocked)
            {
                var lockInfo = lockObj.LockInfo;
                throw new LockOperationException(String.Format(CultureInfo.CurrentCulture, Resources.Error_OperationLockTimeout, operationName, lockInfo.OperationName, lockInfo.AcquiredDateTime));
            }

            try
            {
                return await operation();
            }
            finally
            {
                lockObj.Release();
            }
        }

        private static async Task<bool> WaitToLockAsync(IOperationLock lockObj, string operationName, TimeSpan timeout)
        {
            var elapsed = TimeSpan.Zero;

            while (!lockObj.Lock(operationName))
            {
                if (elapsed >= timeout)
                {
                    return false;
                }

                await Task.Delay(_sleepInterval);
                elapsed += _sleepInterval;
            }

            return true;
        }
    }
}
