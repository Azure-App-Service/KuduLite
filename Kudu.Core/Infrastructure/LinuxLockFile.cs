using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.SourceControl;
using Kudu.Core.Tracing;
using Mono.Unix.Native;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kudu.Core.Infrastructure
{
    public class LinuxLockFile : IOperationLock
    {
        
        private const int LOCK_SH = 1;
        private const int LOCK_EX = 2;
        private const int LOCK_UN = 8;
        private const int LOCK_NB = 4;

        public readonly string _lockFile;
        int _fd;
        
        
        private const string NotEnoughSpaceText = "There is not enough space on the disk.";
        private readonly ITraceFactory _traceFactory;
        
        
        // lock must be acquired without any error
        // default is false - meaning allow lock to be acquired during
        // file system readonly or disk full period.
        private readonly bool _ensureLock;

        private ConcurrentQueue<QueueItem> _lockRequestQueue;

        public bool IsHeld
        {
            get
            {
                try
                {
                    AcquireExclusiveLock();
                    Release();
                    return false;
                }
                catch (Exception)
                {
                    return true;
                }
            }
        }


        public LinuxLockFile(string path)
            : this(path, NullTracerFactory.Instance)
        {
            _lockRequestQueue = new ConcurrentQueue<QueueItem>();
        }

        /// <summary>
        /// Uses unix flock API to do Inter process synchronization.
        /// flock is able to acquire a reader writer lock using a file descriptor
        /// as key. Refer: https://linux.die.net/man/2/flock
        /// </summary>
        /// <param name="lockFile"></param>
        public LinuxLockFile(string path, ITraceFactory traceFactory, bool ensureLock = false)
        {
           
            _traceFactory = traceFactory;
            _ensureLock = ensureLock;
            _lockFile = Path.GetFullPath(path);

            FileSystemHelpers.EnsureDirectory("/home/site");
            FileSystemHelpers.EnsureDirectory(Path.GetDirectoryName(_lockFile));
            
        }
        
        public OperationLockInfo LockInfo
        {
            get
            {
                if (IsHeld)
                {
                    var info = ReadLockInfo();
                    _traceFactory.GetTracer().Trace("Lock '{0}' is currently held by '{1}' operation started at {2}.", _lockFile, info.OperationName, info.AcquiredDateTime);
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
                return JsonConvert.DeserializeObject<OperationLockInfo>(FileSystemHelpers.ReadAllTextFromFile(_lockFile)) ?? new OperationLockInfo { OperationName = "unknown" };
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
        
        public virtual void OnLockAcquired()
        {
            // no-op
        }

        public virtual void OnLockRelease()
        {
            // no-op
        }
        
        public bool Lock(string operationName)
        {
            Stream lockStream = null;
            try
            {
                
                //FileSystemHelpers.EnsureDirectory(Path.GetDirectoryName(_lockFile));

                //lockStream = FileSystemHelpers.OpenFile(_lockFile, FileMode.Create, FileAccess.Write, FileShare.Read);

                //WriteLockInfo(operationName, lockStream);

                AcquireExclusiveLock();
                
                lockStream = null;
                //OnLockRelease();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                if (!_ensureLock)
                {
                    // if it is ReadOnly file system, we will skip the lock
                    // which will enable all read action
                    // for write action, it will fail with UnauthorizedAccessException when perform actual write operation
                    //      There is one drawback, previously for write action, even acquire lock will fail with UnauthorizedAccessException,
                    //      there will be retry within given timeout. so if exception is temporary, previous`s implementation will still go thru.
                    //      While right now will end up failure. But it is a extreem edge case, should be ok to ignore.
                    return FileSystemHelpers.IsFileSystemReadOnly();
                }
            }
            catch (IOException ex)
            {
                if (!_ensureLock)
                {
                    // if not enough disk space, no one has the lock.
                    // let the operation thru and fail where it would try to get the file
                    return ex.Message.Contains(NotEnoughSpaceText);
                }
            }
            catch (Exception ex)
            {
                TraceIfUnknown(ex);
            }
            finally
            {
                if (lockStream != null)
                {
                    lockStream.Close();
                }
            }

            return false;
        }

        /// <summary>
        /// Returns a lock right away or waits asynchronously until a lock is available.
        /// </summary>
        /// <returns>Task indicating the task of acquiring the lock.</returns>
        public Task LockAsync(string operationName)
        {

            // See if we can get the lock -- if not then enqueue lock request.
            if (Lock(operationName))
            {
                return Task.FromResult(true);
            }

            QueueItem item = new QueueItem(operationName);
            _lockRequestQueue.Enqueue(item);
            return item.HasLock.Task;
        }


        public void AcquireSharedLock()
        {
            int ret = flock(_fd, LOCK_SH | LOCK_NB);
            if (ret == -1)
            {
                Errno error = Mono.Unix.Native.Syscall.GetLastError();
                throw new Exception("AcquireSharedLock flock returned: " + error.ToString());
            }
        }

        public void AcquireExclusiveLock()
        {
            _fd = Mono.Unix.Native.Syscall.open(
                _lockFile,
                Mono.Unix.Native.OpenFlags.O_CREAT | 
                Mono.Unix.Native.OpenFlags.O_WRONLY | 
                Mono.Unix.Native.OpenFlags.O_APPEND |
                Mono.Unix.Native.OpenFlags.O_NONBLOCK,
                FilePermissions.ACCESSPERMS);

            if (_fd == -1)
            {
                _fd = 0;
                Errno error = Mono.Unix.Native.Syscall.GetLastError();
                throw new Exception("FileRWLock: file open returned:  " + error.ToString());
            }
            
            int ret = flock(_fd, LOCK_EX | LOCK_NB);
            if (ret == -1)
            {
                Errno error = Mono.Unix.Native.Syscall.GetLastError();
                throw new Exception("AcquireExLock flock returned: " + error.ToString());
            }
            
            if (_fd != 0)
            {
                Mono.Unix.Native.Syscall.close(_fd);
                _fd = 0;
            }
            //LinuxEventProvider.LogInfo("Bindings", "Acquired exclusive lock on: " + _windowsLockFile);
        }

        public void Release()
        {
            int ret = flock(_fd, LOCK_UN | LOCK_NB);
            if (ret == -1)
            {
                Errno error = Mono.Unix.Native.Syscall.GetLastError();
                throw new Exception("AcquireExLock flock returned: " + error.ToString());
            }
            //LinuxEventProvider.LogInfo("Bindings", "Released exclusive lock on: " + _windowsLockFile);
            if (_fd != 0)
            {
                Mono.Unix.Native.Syscall.close(_fd);
                _fd = 0;
            }
        }

        public IRepositoryFactory RepositoryFactory { get; set; }


        /// <summary>
        /// When a lock file change has been detected we check whether there are queued up lock requests.
        /// If so then we attempt to get the lock and dequeue the next request.
        /// </summary>
        private void OnLockReleasedInternal(object sender, FileSystemEventArgs e)
        {
            if (!_lockRequestQueue.IsEmpty)
            {                
                QueueItem item;
                if (_lockRequestQueue.TryPeek(out item) && Lock(item.OperationName))
                {
                    if (!_lockRequestQueue.IsEmpty)
                    {
                        if (!_lockRequestQueue.TryDequeue(out item))
                        {
                            string msg = String.Format(Resources.Error_AsyncLockNoLockRequest, _lockRequestQueue.Count);
                            _traceFactory.GetTracer().TraceError(msg);
                            Release();
                        }

                        if (!item.HasLock.TrySetResult(true))
                        {
                            _traceFactory.GetTracer().TraceError(Resources.Error_AsyncLockRequestCompleted);
                            Release();
                        }
                    }
                    else
                    {
                        Release();
                    }
                }
            }
        }

        /*
        ~FileRWLock()
        {
            if (_fd != 0)
            {
                Mono.Unix.Native.Syscall.close(_fd);
                _fd = 0;
            }
        }
        */

        public void InitializeAsyncLocks()
        {
            //
            // Create the file.
            //

            int fd = Mono.Unix.Native.Syscall.open(
                    _lockFile,
                    Mono.Unix.Native.OpenFlags.O_CREAT | Mono.Unix.Native.OpenFlags.O_WRONLY | Mono.Unix.Native.OpenFlags.O_APPEND | Mono.Unix.Native.OpenFlags.O_NONBLOCK,
                    FilePermissions.ACCESSPERMS);

            if (fd == -1)
            {
                Errno error = Mono.Unix.Native.Syscall.GetLastError();
                throw new Exception("FileRWLock: file open returned:  " + error.ToString());
            }

            _fd = fd;
            
            //
            // close the file.
            //

            Mono.Unix.Native.Syscall.close(fd);

            uint groupId = (uint) Mono.Unix.Native.Syscall.getgrnam("kudu_group").gr_gid;

            //
            // chown the file file
            //

            Mono.Unix.Native.Syscall.chown(_lockFile, Mono.Unix.Native.Syscall.getuid(), groupId);

            //
            // setup permissions 770 on the file.
            //

            Mono.Unix.Native.FilePermissions permissions =
                Mono.Unix.Native.FilePermissions.S_IRWXU |
                Mono.Unix.Native.FilePermissions.S_IRGRP |
                Mono.Unix.Native.FilePermissions.S_IWGRP |
                Mono.Unix.Native.FilePermissions.S_IXGRP;

            //
            // chmod the file.
            //

            Mono.Unix.Native.Syscall.chmod(_lockFile, permissions);
        }
        
        // we only write the lock info at lock's enter since
        // lock file will be cleaned up at release
        private static void WriteLockInfo(string operationName, Stream lockStream)
        {
            var json = JObject.FromObject(new OperationLockInfo
            {
                OperationName = operationName,
                StackTrace = System.Environment.StackTrace,
                InstanceId = InstanceIdUtility.GetShortInstanceId()
            });

            var bytes = Encoding.UTF8.GetBytes(json.ToString());
            lockStream.Write(bytes, 0, bytes.Length);
            lockStream.Flush();
        }
        
        private void TraceIfUnknown(Exception ex)
        {
            if (!(ex is IOException) && !(ex is UnauthorizedAccessException))
            {
                // trace unexpected exception
                _traceFactory.GetTracer().TraceError(ex);
            }
        }

        [DllImport("libc.so.6")]
        private static extern int flock(int fd, int mode);
        
        
        private class QueueItem
        {
            public QueueItem(string operationName)
            {
                OperationName = operationName;
                HasLock = new TaskCompletionSource<bool>();
            }

            public string OperationName { get; private set; }

            public TaskCompletionSource<bool> HasLock { get; private set; }
        }
        
    }
    
    
}