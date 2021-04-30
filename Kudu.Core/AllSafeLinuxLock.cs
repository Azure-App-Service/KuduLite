using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.SourceControl;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kudu.Core
{
    /// <summary>
    /// This 
    /// </summary>
    public class AllSafeLinuxLock :IOperationLock
    {
        private ITraceFactory _traceFactory;
        private static readonly string locksPath = "/home/site/locks";
	    private const int defaultLockTimeout = 1500; //in seconds
        private string defaultMsg = Resources.DeploymentLockOccMsg;
        private static object LockExpiryLock = new object();
        private static DateTime LockExpiry = DateTime.MinValue;
        private string Msg;
        public AllSafeLinuxLock(string path, ITraceFactory traceFactory)
        {
            _traceFactory = traceFactory;
        }
        public bool IsHeld
        {
            get
            {
                Exception exception = null;
                if (FileSystemHelpers.DirectoryExists(locksPath+"/deployment"))
                {
                    try
                    {
                        var ret = IsLockValid();
                        return ret;
                    }
                    catch (Exception ex)
                    {
                        _traceFactory.GetTracer().Trace("Error determining if deployment lock is valid");
                        _traceFactory.GetTracer().TraceError(ex);
                        // Exception where file is corrupt
                        // Wait for a second, if the file is being written
                        //Console.WriteLine("IsHeld - There was an Exception - Sleeping for a second ");
                        Thread.Sleep(1000);
                        exception = ex;
                        if(IsLockValid())
                        {
                            exception = null;
                            return true;
                        }
                    }
                    finally
                    {
                        // There is some problem with reading the lock info file
                        // Avoid deadlock by releasing this lock/removing the dir
                        if (exception!=null)
                        {
                            _traceFactory.GetTracer().Trace("IsHeld - there were exceptions twice -releasing the lock - ie deleting the lock directory");
                            FileSystemHelpers.DeleteDirectorySafe(locksPath+"/deployment");
                        }
                    }
                }
                return false;
            }
        }

        private static bool IsLockValid()
        {
            if (!FileSystemHelpers.FileExists(locksPath+"/deployment/info.lock")) return false;

            // No need to serialize lock expiry object again until the
            // lock expiry period, we would use local cache instead
            // At this point we have already checked for the folder presence
            // hence to avoid the I/O, don't serialize the lock info until
            // folder is cleaned up
            lock (LockExpiryLock)
            {
                if (LockExpiry > DateTime.UtcNow)
                {
                    return true;
                }
            }

            var lockInfo = JObject.Parse(File.ReadAllText(locksPath+"/deployment/info.lock"));
            var workerId = lockInfo[$"heldByWorker"].ToString();
            var expStr = lockInfo[$"lockExpiry"].ToString();
            
            //Should never have null expiry
            if (string.IsNullOrEmpty(expStr))
            {
                Console.WriteLine("IsLockValid - Null Expiry | This is Bad");
                FileSystemHelpers.DeleteDirectorySafe(locksPath+"/deployment");
                return false;   
            }
            
            var exp = Convert.ToDateTime(expStr);
            lock (LockExpiryLock)
            {
                LockExpiry = exp;
            }
            if (exp > DateTime.UtcNow)
            {
                return true;
            }
            //Console.WriteLine("IsLockValid - Lock is Past expiry - Deleting Lock Dir");
            FileSystemHelpers.DeleteDirectorySafe(locksPath+"/deployment");
            return false;
        }

        private static void CreateLockInfoFile(string operationName)
        {
            FileSystemHelpers.CreateDirectory(locksPath+"/deployment");
            var lockInfo = new LinuxLockInfo();
            lockInfo.heldByPID = Process.GetCurrentProcess().Id;
            lockInfo.heldByTID = Thread.CurrentThread.ManagedThreadId;
            lockInfo.heldByWorker = System.Environment.GetEnvironmentVariable(Constants.AzureWebsiteInstanceId);
            lockInfo.heldByOp = operationName;
            lockInfo.lockExpiry = DateTime.UtcNow.AddSeconds(defaultLockTimeout);
            var json = JsonConvert.SerializeObject(lockInfo);
            FileSystemHelpers.WriteAllText(locksPath+"/deployment/info.lock",json);
        }
        
        public OperationLockInfo LockInfo
        {
            get
            {
                if (!FileSystemHelpers.FileExists(locksPath + "/deployment/info.lock"))
                {
                    return null;
                }

                try
                {
                    var lockInfo = JObject.Parse(File.ReadAllText(locksPath + "/deployment/info.lock"));
                    var expStr = lockInfo["lockExpiry"].ToString();

                    //Should never have null expiry
                    if (string.IsNullOrEmpty(expStr) || !DateTime.TryParse(expStr, out DateTime exp))
                    {
                        Console.WriteLine($"LockInfo: Invalid lockExpiry found: {expStr}");
                        return null;
                    }

                    var opLockInfo = new OperationLockInfo();

                    opLockInfo.AcquiredDateTime = exp.AddSeconds(-defaultLockTimeout).ToString("o");
                    opLockInfo.OperationName = lockInfo["heldByOp"].ToString();
                    opLockInfo.InstanceId = lockInfo["heldByWorker"].ToString();

                    return opLockInfo;
                }
                catch(Exception ex)
                {
                    _traceFactory.GetTracer().TraceError(ex);
                    return null;
                }
            }
        }
        
        public bool Lock(string operationName)
        {
            _traceFactory.GetTracer().Trace("Acquiring Deployment Lock");
            if (FileSystemHelpers.DirectoryExists(locksPath+"/deployment"))
            {
                // Directory exists implies a lock exists
                // Either lock info is still being written or it exists
                // If it exists check the expiry
                if (IsHeld)
                {
                    _traceFactory.GetTracer().Trace("Cannot Acquire Deployment Lock already held");
                    return false;
                }
            }
            CreateLockInfoFile(operationName);
            _traceFactory.GetTracer().Trace("Acquired Deployment Lock");
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
            lock (LockExpiryLock)
            {
                LockExpiry = DateTime.MinValue;
            }
            if (FileSystemHelpers.DirectoryExists(locksPath+"/deployment"))
            {
                //Console.WriteLine("Releasing Lock - RemovingDir");
                _traceFactory.GetTracer().Trace("Releasing Deployment Lock");
                FileSystemHelpers.DeleteDirectorySafe(locksPath+"/deployment");
                
            }
            else
            {
                _traceFactory.GetTracer().Trace("Releasing Deployment Lock - There is NO LOCK HELD | ERROR");
            }
        }

        public string GetLockMsg()
        {
            //throw new NotImplementedException();
            if(Msg == null || "".Equals(Msg))
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
                return "Expiry: "+lockExpiry+"; PID : "+heldByPID+" ; TID : "+heldByTID+" ; OP: "+heldByOp+" ; Worker : "+heldByWorker;
            }
        }
    }
}
