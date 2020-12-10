using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.SourceControl;
using Kudu.Core.Helpers;
using Kudu.Core.Tracing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kudu.Core.Infrastructure
{
    /// <summary>
    /// A thread safe/multi application safe lock file.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable", Justification = "Because of a bug in Ninject we can't make this disposable as it would otherwise get disposed on every request.")]
    public class WindowsLockFile : IOperationLock
    {
        private const string NotEnoughSpaceText = "There is not enough space on the disk.";
        private readonly string _path;
        private readonly ITraceFactory _traceFactory;

        // lock must be acquired without any error
        // default is false - meaning allow lock to be acquired during
        // file system readonly or disk full period.
        private readonly bool _ensureLock;

        private ConcurrentQueue<QueueItem> _lockRequestQueue;
        private FileSystemWatcher _lockFileWatcher;

        private Stream _lockStream;

        public WindowsLockFile(string path)
            : this(path, NullTracerFactory.Instance)
        {
        }

        public WindowsLockFile(string path, ITraceFactory traceFactory, bool ensureLock = false)
        {
            _path = Path.GetFullPath(path);
            _traceFactory = traceFactory;
            _ensureLock = ensureLock;
        }

        public OperationLockInfo LockInfo
        {
            get
            {
                if (IsHeld)
                {
                    var info = ReadLockInfo();
                    _traceFactory.GetTracer().Trace("Lock '{0}' is currently held by '{1}' operation started at {2}.", _path, info.OperationName, info.AcquiredDateTime);
                    return info;
                }

                // lock info represent no owner
                return new OperationLockInfo();
            }
        }

        public virtual void InitializeAsyncLocks()
        {
            _lockRequestQueue = new ConcurrentQueue<QueueItem>();

            FileSystemHelpers.EnsureDirectory(Path.GetDirectoryName(_path));

            // Set up lock file watcher. Note that depending on how the file is accessed the file watcher may generate multiple events.
            _lockFileWatcher = new FileSystemWatcher(Path.GetDirectoryName(_path), Path.GetFileName(_path));
            _lockFileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
            _lockFileWatcher.Changed += OnLockReleasedInternal;
            _lockFileWatcher.Deleted += OnLockReleasedInternal;
            _lockFileWatcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// Because of a bug in Ninject in how it disposes objects in the global scope for each request
        /// we can't use Dispose to shut down the file system watcher. Otherwise this would get disposed
        /// on every request.
        /// </summary>
        public void TerminateAsyncLocks()
        {
            if (_lockFileWatcher != null)
            {
                _lockRequestQueue = null;
                _lockFileWatcher.EnableRaisingEvents = false;
                _lockFileWatcher.Dispose();
                _lockFileWatcher = null;
            }
        }

        public virtual bool IsHeld
        {
            get
            {
                // If there's no file then there's no process holding onto it
                if (!FileSystemHelpers.FileExists(_path))
                {
                    _traceFactory.GetTracer().Trace("Lock '{0}' no file. no one holding the lock", _path);
                    return false;
                }

                try
                {
                    // If there is a file, lets see if someone has an open handle to it, or if it's
                    // just hanging there for no reason
                    using (FileSystemHelpers.OpenFile(_path, FileMode.Open, FileAccess.Read, FileShare.Read)) { }
                }
                catch (UnauthorizedAccessException)
                {
                    _traceFactory.GetTracer().Trace("Lock '{0}' unauthorized access exception to open the lock file. returning false", _path);
                    // if it is ReadOnly file system, we will skip the lock
                    // which will enable all read action
                    // for write action, it will fail with UnauthorizedAccessException when perform actual write operation
                    //      There is one drawback, previously for write action, even acquire lock will fail with UnauthorizedAccessException,
                    //      there will be retry within given timeout. so if exception is temporary, previous`s implementation will still go thru.
                    //      While right now will end up failure. But it is a extreem edge case, should be ok to ignore.
                    return !FileSystemHelpers.IsFileSystemReadOnly();
                }
                catch (IOException ex)
                {
                    // if not enough disk space, no one has the lock.
                    // let the operation thru and fail where it would try to get the file
                    return !ex.Message.Contains(NotEnoughSpaceText);
                }
                catch (Exception ex)
                {
                    TraceIfUnknown(ex);
                    return true;
                }

                // cleanup inactive lock file.  technically, it is not needed
                // we just want to see the lock folder is clean, if no active lock.
                DeleteFileSafe();

                return false;
            }
        }

        public virtual bool Lock(string operationName)
        {
            Stream lockStream = null;
            try
            {
                _traceFactory.GetTracer().Trace("Lock '{0}' - trying to lock", _path);
                FileSystemHelpers.EnsureDirectory(Path.GetDirectoryName(_path));
                if (FileSystemHelpers.FileExists(_path))
                {
                    _traceFactory.GetTracer().Trace("Lock '{0}' lock file exists. Returning false to lock", _path);
                    return false;
                }
                lockStream = FileSystemHelpers.OpenFile(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _traceFactory.GetTracer().Trace("Lock '{0}' writing lock file", _path);
                WriteLockInfo(operationName, lockStream);

                OnLockAcquired();

                _lockStream = lockStream;
                lockStream = null;

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
                _traceFactory.GetTracer().Trace("Lock '{0}' IO Exception: {1}", _path, ex.ToString());
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
                _traceFactory.GetTracer().Trace("Lock '{0}' CLOSING lockstream", _path);
                if (lockStream != null)
                {
                    lockStream.Close();
                }
            }

            return false;
        }

        public virtual void OnLockAcquired()
        {
            // no-op
        }

        protected virtual void OnLockRelease()
        {
            // no-op
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

        private OperationLockInfo ReadLockInfo()
        {
            try
            {
                return JsonConvert.DeserializeObject<OperationLockInfo>(FileSystemHelpers.ReadAllTextFromFile(_path)) ?? new OperationLockInfo { OperationName = "unknown" };
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

        /// <summary>
        /// Returns a lock right away or waits asynchronously until a lock is available.
        /// </summary>
        /// <returns>Task indicating the task of acquiring the lock.</returns>
        public Task LockAsync(string operationName)
        {
            if (_lockFileWatcher == null)
            {
                throw new InvalidOperationException(Resources.Error_AsyncLockNotInitialized);
            }

            // See if we can get the lock -- if not then enqueue lock request.
            if (Lock(operationName))
            {
                return Task.FromResult(true);
            }

            QueueItem item = new QueueItem(operationName);
            _lockRequestQueue.Enqueue(item);
            return item.HasLock.Task;
        }

        public virtual void Release()
        {
            _traceFactory.GetTracer().Trace("Lock '{0}' RELEASE LOCK FILE", _path);

            // Normally, this should never be null here, but currently some LiveScmEditorController code calls Release() incorrectly
            if (_lockStream == null)
            {
                OnLockRelease();
                return;
            }

            var temp = _lockStream;
            _lockStream = null;
            temp.Close();

            // cleanup inactive lock file. technically, it is not needed
            // we just want to see the lock folder is clean, if no active lock.
            DeleteFileSafe();

            OnLockRelease();
        }

        public IRepositoryFactory RepositoryFactory { get; set; }

        // we cannot use FileSystemHelpers.DeleteFileSafe.
        // it does not handled IOException due to 'file in used'.
        private void DeleteFileSafe()
        {
            _traceFactory.GetTracer().Trace("Lock '{0}' DELETING LOCK FILE", _path);

            // Only clean up lock on Windows Env
            try
            {
                FileSystemHelpers.DeleteFile(_path);
                OperationManager.Attempt(() =>
                    // throws exception if file is still present
                    TryRemovedLockFile()
                , 10, 250);
            }
            catch (Exception ex)
            {
                TraceIfUnknown(ex);
            }
        }

        private void TraceIfUnknown(Exception ex)
        {
            if (!(ex is IOException) && !(ex is UnauthorizedAccessException))
            {
                // trace unexpected exception
                _traceFactory.GetTracer().TraceError(ex);
            }
        }

        private void TryRemovedLockFile()
        {
            if (!FileSystemHelpers.FileExists(_path))
            {
                return;
            }
            FileSystemHelpers.DeleteFile(_path);
            // check if file still exists
            if (FileSystemHelpers.FileExists(_path))
            {
                throw new Exception("Tried removing but the lock file still exists, would retry the removal");
            }
        }

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