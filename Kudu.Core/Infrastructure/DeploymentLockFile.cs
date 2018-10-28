using System;
using System.Diagnostics;
using System.Threading;
using Kudu.Contracts.SourceControl;
using Kudu.Core.Helpers;
using Kudu.Core.SourceControl;
using Kudu.Core.Tracing;

namespace Kudu.Core.Infrastructure
{
    /// <summary>
    /// Specific to deployment lock.
    /// </summary>
    public class DeploymentLockFile : WindowsLockFile
    {
        private static readonly ReaderWriterLockSlim rwl = new ReaderWriterLockSlim();
        private readonly string _path;
        private static LinuxLockFile _linuxLock;
        private readonly bool _isLinux;
        private static DeploymentLockFile _deploymentLockFile;
        private static bool isFlockHeld;
        private static bool isFileWatcherLockHeld;
        private static bool isReaderWriterLockHeld;

        public static DeploymentLockFile GetInstance(string path, ITraceFactory traceFactory)
        {
            if (_deploymentLockFile == null)
            {
                _deploymentLockFile = new DeploymentLockFile(path,traceFactory);
            }

            return _deploymentLockFile;
        }

        private DeploymentLockFile(string path, ITraceFactory traceFactory) : base(path,traceFactory)
        {
            _path = path;
            /*
            if (!OSDetector.IsOnWindows())
            {
                _linuxLock = new LinuxLockFile(path+"_2",traceFactory);
                _isLinux = true;
            }
            */
        }

        public override bool IsHeld
        {
            get
            {
                //bool flockHeld = false;
                Console.WriteLine("\n\n\n\nISHeld? "+"  write lock held "+rwl.IsWriteLockHeld);
                Console.WriteLine("TID  "+Thread.CurrentThread.ManagedThreadId);
                Console.WriteLine("PID  "+Process.GetCurrentProcess().Id);
                Console.WriteLine("IsHeld"+base.IsHeld);
                //Console.WriteLine(System.Environment.StackTrace);
                /*
                if (rwl.IsWriteLockHeld)
                {
                    Console.WriteLine("RWL Lock Held");
                }
                
                if (_isLinux)
                {
                    //Console.WriteLine("Checking if flock is held");
                    //flockHeld = _linuxLock.IsHeld;
                    //Console.WriteLine("Checking if flock is held : "+flockHeld);
                }
                // Always check again, don't use bools as request could be on a different
                // worker
                */
                //return rwl.IsWriteLockHeld || base.IsHeld;
                return base.IsHeld;
            }
        }

        public override bool Lock(string operationName)
        {
            Console.WriteLine("\n\n\n\n ACQUIRE Deployment Lock"+" |  write lock held : "+rwl.IsWriteLockHeld+ " read lock held : "+rwl.IsReadLockHeld);
            Console.WriteLine("TID  "+Thread.CurrentThread.ManagedThreadId);
            Console.WriteLine("PID  "+Process.GetCurrentProcess().Id);
            Console.WriteLine(System.Environment.StackTrace);
            if (IsHeld)
            {
                return false;
            }
            return base.Lock(operationName);
            /*
            try
            {
                Console.WriteLine(System.Environment.StackTrace);
                if (!FileSystemHelpers.FileExists(_path))
                {
                    FileSystemHelpers.CreateFile(_path);
                }
                if (rwl.IsWriteLockHeld)
                {
                    return false;
                }

                isReaderWriterLockHeld = rwl.TryEnterWriteLock(TimeSpan.Zero);
                isFileWatcherLockHeld = base.Lock(operationName);
                //isFileWatcherLockHeld = true;
                if (_isLinux)
                {
                    //isFlockHeld = _linuxLock.Lock(operationName);
                    isFlockHeld = true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(" msg : "+e.Message);
                Console.WriteLine("trace : "+e.StackTrace);
                throw;
            }
            finally
            {
                if ((!OSDetector.IsOnWindows()&&!isFlockHeld) || !isFileWatcherLockHeld || !isReaderWriterLockHeld)
                {
                    //Release flock if couldn't acq File Lock or Rwl Lock
                    if (!OSDetector.IsOnWindows()&& isFlockHeld)
                    {
                        Console.WriteLine("Error with Lock - Releasing FLock");
                        _linuxLock.Release();
                    }

                    if (isReaderWriterLockHeld)
                    {
                        Console.WriteLine("Error with Lock - Releasing ReaderWriter Lock");
                        rwl.ExitWriteLock();
                    }

                    if (isFileWatcherLockHeld)
                    {
                        Console.WriteLine("Error with Lock - Releasing FileWatch Lock");
                        base.Release();
                    }
                        
                }
            }
            
            return OSDetector.IsOnWindows()? 
                isReaderWriterLockHeld && isFileWatcherLockHeld : isReaderWriterLockHeld && isFileWatcherLockHeld && isFlockHeld;
                */
        }

        public override void Release()
        {
            Console.WriteLine("\n\n\n\n\n\nRELEASE");
            //Console.WriteLine("  write lock held "+rwl.IsWriteLockHeld+ " read lock held : "+rwl.IsReadLockHeld);
            Console.WriteLine("TID  "+Thread.CurrentThread.ManagedThreadId);
            Console.WriteLine("PID  "+Process.GetCurrentProcess().Id);
            Console.WriteLine(System.Environment.StackTrace);
            Console.WriteLine("Releasing from Deployment Lock File  "+_path);
            base.Release();
            /*
            if (isReaderWriterLockHeld)
            {
                Console.WriteLine("Releasing Reader Writer Lock");
                rwl.ExitWriteLock();
            }
            if (isFileWatcherLockHeld)
            {
                Console.WriteLine("Releasing File Watcher Lock");
                base.Release();
            }
            
            if (_isLinux && isFlockHeld)
            {
                Console.WriteLine("Releasing FLock");
                //_linuxLock.Release();
            }
            
            isFileWatcherLockHeld = false;
            isFlockHeld = false;
            isReaderWriterLockHeld = false;
            Console.WriteLine("Released from Deployment Lock File  " + _path);
            Console.WriteLine("\n\n\n\n\n\n");
            */
        }

        
            public void OnLockAcquired()
        {
            IRepositoryFactory repositoryFactory = RepositoryFactory;
            if (repositoryFactory != null)
            {
                IRepository repository = repositoryFactory.GetRepository();
                if (repository != null)
                {
                    // Clear any left over repository-related lock since we have the actual lock
                    repository.ClearLock();
                }
            }
        }
    }
}