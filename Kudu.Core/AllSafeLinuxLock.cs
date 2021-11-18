using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.SourceControl;
using Kudu.Core.Infrastructure;
using Kudu.Core.K8SE;
using Kudu.Core.Tracing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kudu.Core
{
    /// <summary>
    /// This 
    /// </summary>
    public class AllSafeLinuxLock : IOperationLock
    {
        private ITraceFactory _traceFactory;
        private readonly string locksPath = "/home/site/locks"; //The default lock path when the lock is not per site.
        private const int lockTimeout = 1200; //in seconds
        private string defaultMsg = Resources.DeploymentLockOccMsg;
        private string Msg;
        public AllSafeLinuxLock(string path, ITraceFactory traceFactory)
        {
            _traceFactory = traceFactory;
            if (K8SEDeploymentHelper.IsBuildJob() || K8SEDeploymentHelper.UseBuildJob())
            {
                //Use per site lock after build job is enabled.
                locksPath = path;
            }
        }

        public string LocksPath
        {
            get
            {
                return locksPath;
            }
        }

        public bool IsHeld
        {
            get
            {
                Exception exception = null;
                if (FileSystemHelpers.DirectoryExists(locksPath + "/deployment"))
                {
                    //Console.WriteLine("IsHeld - DirectoryExists");
                    try
                    {
                        //Console.WriteLine("IsHeld - Trying to read the lock data");
                        var ret = IsLockValid();
                        //Console.WriteLine("IsHeld - IsLockValid returned "+ret);
                        return ret;
                    }
                    catch (Exception ex)
                    {
                        // Exception where file is corrupt
                        // Wait for a second, if the file is being written
                        //Console.WriteLine("IsHeld - There was an Exception - Sleeping for a second ");
                        Thread.Sleep(1000);
                        exception = ex;
                        return IsLockValid();
                    }
                    finally
                    {
                        // There is some problem with reading the lock info file
                        // Avoid deadlock by releasing this lock/removing the dir
                        if (exception != null)
                        {
                            //Console.WriteLine("IsHeld - there were exceptions twice -releasing the lock - ie deleting the lock directory");
                            FileSystemHelpers.DeleteDirectorySafe(locksPath + "/deployment");
                        }
                    }
                }
                return false;
            }
        }

        private bool IsLockValid()
        {
            //Console.WriteLine("IsLockValid - InfoFileExists "+FileSystemHelpers.FileExists(locksPath+"/deployment/info.lock"));
            if (!FileSystemHelpers.FileExists(locksPath + "/deployment/info.lock")) return false;
            var lockInfo = JObject.Parse(File.ReadAllText(locksPath + "/deployment/info.lock"));
            //Console.WriteLine(lockInfo);
            var workerId = lockInfo[$"heldByWorker"].ToString();
            var expStr = lockInfo[$"lockExpiry"].ToString();
            //Console.WriteLine("IsLockValid - LockExpiry "+expStr);
            //Console.WriteLine("IsLockValid - HeldByWorker "+workerId);

            //Should never have null expiry
            if (string.IsNullOrEmpty(expStr))
            {
                Console.WriteLine("IsLockValid - Null Expiry | This is Bad");
                FileSystemHelpers.DeleteDirectorySafe(locksPath + "/deployment");
                return false;
            }

            var exp = Convert.ToDateTime(expStr.ToString());

            if (exp > DateTime.UtcNow)
            {
                //Console.WriteLine("Expiry Time - "+exp);
                //Console.WriteLine("IsLockValid - "+DateTime.UtcNow);
                //Console.WriteLine("IsLockValid - Within 5 min expiry");
                return true;
            }
            //Console.WriteLine("IsLockValid - Lock is Past expiry - Deleting Lock Dir");
            FileSystemHelpers.DeleteDirectorySafe(locksPath + "/deployment");
            return false;
        }

        private void CreateLockInfoFile(string operationName)
        {
            FileSystemHelpers.CreateDirectory(locksPath + "/deployment");
            //Console.WriteLine("CreatingLockDir - Created Actually");
            var lockInfo = new LinuxLockInfo();
            lockInfo.heldByPID = Process.GetCurrentProcess().Id;
            lockInfo.heldByTID = Thread.CurrentThread.ManagedThreadId;
            lockInfo.heldByWorker = System.Environment.GetEnvironmentVariable(Constants.AzureWebsiteInstanceId);
            lockInfo.heldByOp = operationName;
            lockInfo.lockExpiry = DateTime.UtcNow.AddSeconds(lockTimeout);
            //Console.WriteLine("CreatingLockDir - LockInfoObj : "+lockInfo);
            var json = JsonConvert.SerializeObject(lockInfo);
            FileSystemHelpers.WriteAllText(locksPath + "/deployment/info.lock", json);
        }

        public OperationLockInfo LockInfo
        {
            get
            {
                if (IsHeld)
                {
                    var info = ReadLockInfo();
                    _traceFactory.GetTracer().Trace("Lock '{0}' is currently held by '{1}' operation started at {2}.", locksPath + "/deployment/info.lock", info.OperationName, info.AcquiredDateTime);
                    return info;
                }

                // lock info represent no owner
                return new OperationLockInfo();
            }
        }


        private OperationLockInfo ReadLockInfo()
        {
            try
            {
                return JsonConvert.DeserializeObject<OperationLockInfo>(FileSystemHelpers.ReadAllTextFromFile(locksPath + "/deployment/info.lock")) ?? new OperationLockInfo { OperationName = "unknown" };
            }
            catch (Exception ex)
            {
                _traceFactory.GetTracer().TraceError(ex);
                return new OperationLockInfo
                {
                    OperationName = "unknown",
                    StackTrace = ex.ToString()
                };
            };
        }

        public bool Lock(string operationName)
        {
            if (FileSystemHelpers.DirectoryExists(locksPath + "/deployment"))
            {
                // Directory exists implies a lock exists
                // Either lock info is still being written or it exists
                // If it exists check the expiry
                if (IsHeld)
                {
                    //Console.WriteLine("LockOp - Lock Already Held");
                    return false;
                }
            }
            //Console.WriteLine("LockOp - Creating Lock");
            CreateLockInfoFile(operationName);
            return true;
        }

        public void InitializeAsyncLocks()
        {
            //no-op
        }

        public IRepositoryFactory RepositoryFactory { get; set; }

        public Task LockAsync(string operationName)
        {
            throw new System.NotImplementedException();
        }

        public void Release()
        {
            if (FileSystemHelpers.DirectoryExists(locksPath + "/deployment"))
            {
                //Console.WriteLine("Releasing Lock - RemovingDir");
                _traceFactory.GetTracer().Trace("Releasing Lock ");
                FileSystemHelpers.DeleteDirectorySafe(locksPath + "/deployment");

            }
            else
            {
                Console.WriteLine("ReleasingLock - There is NO LOCK HELD | ERROR");
            }
        }

        public string GetLockMsg()
        {
            //throw new NotImplementedException();
            if (Msg == null || "".Equals(Msg))
            {
                return defaultMsg;
            }

            return Msg;
        }

        public void SetLockMsg(string msg)
        {
            this.Msg = msg;

        }

        private class LinuxLockInfo
        {
            public DateTime lockExpiry;
            public int heldByPID;
            public int heldByTID;
            public string heldByOp;
            public string heldByWorker;

            public override string ToString()
            {
                return "Expiry: " + lockExpiry + "; PID : " + heldByPID + " ; TID : " + heldByTID + " ; OP: " + heldByOp + " ; Worker : " + heldByWorker;
            }
        }
    }
}
