using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Helpers;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Services.Performance
{
    public class LogStreamManager
    {
        private const string FilterQueryKey = "filter";
        private const string AzureDriveEnabledKey = "AzureDriveEnabled";

        // Azure 3 mins timeout, heartbeat every mins keep alive.
        private static string[] LogFileExtensions = new string[] { ".txt", ".log", ".htm" };

        // TODO Set back to 1 minute
        private static TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

        private readonly object _thisLock = new object();
        private readonly string _logPath;
        private readonly IEnvironment _environment;
        private readonly ITracer _tracer;
        private readonly IOperationLock _operationLock;
        // CORE TODO No longer needed, each task can check context.RequestAborted token
        //private readonly List<ProcessRequestAsyncResult> _results;

        private Dictionary<string, long> _logFiles;
        private IFileSystemWatcher _watcher;
        private Timer _heartbeat;
        private DateTime _lastTraceTime;
        private DateTime _startTime;
        private TimeSpan _timeout;

        // CORE TODO
        //private ShutdownDetector _shutdownDetector;
        private CancellationTokenRegistration _cancellationTokenRegistration;

        // TODO need to grab "path" from request path

        public LogStreamManager(string logPath,
                                IEnvironment environment,
                                IDeploymentSettingsManager settings,
                                ITracer tracer,
                                //ShutdownDetector shutdownDetector,
                                IOperationLock operationLock)
        {
            _logPath = logPath;
            _tracer = tracer;
            _environment = environment;
            //_shutdownDetector = shutdownDetector;
            _timeout = settings.GetLogStreamTimeout();
            _operationLock = operationLock;
            //_results = new List<ProcessRequestAsyncResult>();
        }

        // CORE TODO bind this class as transient (one per request)

        public async Task ProcessRequest(HttpContext context)
        {
            _startTime = DateTime.UtcNow;
            _lastTraceTime = _startTime;

            var stopwatch = Stopwatch.StartNew();
            
            // CORE TODO Shutdown detector registration

            // CORE TODO double check on semantics of this (null vs empty etc);
            var filter = context.Request.Query[FilterQueryKey].ToString();

            // CORE TODO parse from path
            // path route as in logstream/{*path} without query strings
            // string routePath = context.Request.RequestContext.RouteData.Values["path"] as string;

            string path;
            string routePath = "";
            bool enableTrace = false;

            // trim '/'
            routePath = String.IsNullOrEmpty(routePath) ? routePath : routePath.Trim('/');

            // logstream at root
            if (String.IsNullOrEmpty(routePath))
            {
                enableTrace = true;
                path = _logPath;
            }

            var firstPath = routePath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.Equals(firstPath, "Application", StringComparison.OrdinalIgnoreCase))
            {
                enableTrace = true;
            }

            path = FileSystemHelpers.EnsureDirectory(Path.Combine(_logPath, routePath));

            await WriteInitialMessage(context);

            // CORE TODO Get the fsw and keep it in scope here with a using that ends at the end
            //Initialize(path);

            // CORE TODO diagnostics setting for enabling app logging            

            while (!context.RequestAborted.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatInterval);

                var elapsed = stopwatch.Elapsed;

                if (elapsed >= _timeout)
                {
                    var timeoutMsg = String.Format(
                        CultureInfo.CurrentCulture,
                        Resources.LogStream_Timeout,
                        DateTime.UtcNow.ToString("s"),
                        (int)elapsed.TotalMinutes,
                        System.Environment.NewLine);

                    await context.Response.WriteAsync(timeoutMsg);
                    return;
                }
                else if (elapsed >= HeartbeatInterval)
                {
                    var heartbeatMsg = String.Format(
                        CultureInfo.CurrentCulture,
                        Resources.LogStream_Heartbeat,
                        DateTime.UtcNow.ToString("s"),
                        (int)elapsed.TotalMinutes,
                        System.Environment.NewLine);

                    await context.Response.WriteAsync(heartbeatMsg);
                }
            }
        }

        private static Task WriteInitialMessage(HttpContext context)
        {
            var msg = String.Format(CultureInfo.CurrentCulture, Resources.LogStream_Welcome, DateTime.UtcNow.ToString("s"), System.Environment.NewLine);
            return context.Response.WriteAsync(msg);
        }

        //private void Initialize(string path)
        //{
        //    System.Diagnostics.Debug.Assert(_watcher == null, "we only allow one manager per request!");

        //    // initalize _logFiles before the file watcher since file watcher event handlers reference _logFiles
        //    // this mirrors the Reset() where we stop the file watcher before nulling _logFile.
        //    if (_logFiles == null)
        //    {
        //        var logFiles = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        //        foreach (var ext in LogFileExtensions)
        //        {
        //            foreach (var file in Directory.GetFiles(path, "*" + ext, SearchOption.AllDirectories))
        //            {
        //                try
        //                {
        //                    logFiles[file] = new FileInfo(file).Length;
        //                }
        //                catch (Exception ex)
        //                {
        //                    // avoiding racy with providers cleaning up log file
        //                    _tracer.TraceError(ex);
        //                }
        //            }
        //        }

        //        _logFiles = logFiles;
        //    }

        //    if (_watcher == null)
        //    {
        //        IFileSystemWatcher watcher = OSDetector.IsOnWindows()
        //            ? (IFileSystemWatcher)new FileSystemWatcherWrapper(path, includeSubdirectories: true)
        //            : new NaiveFileSystemWatcher(path, LogFileExtensions);
        //        watcher.Changed += new FileSystemEventHandler(DoSafeAction<object, FileSystemEventArgs>(OnChanged, "LogStreamManager.OnChanged"));
        //        watcher.Deleted += new FileSystemEventHandler(DoSafeAction<object, FileSystemEventArgs>(OnDeleted, "LogStreamManager.OnDeleted"));
        //        watcher.Renamed += new RenamedEventHandler(DoSafeAction<object, RenamedEventArgs>(OnRenamed, "LogStreamManager.OnRenamed"));
        //        watcher.Error += new ErrorEventHandler(DoSafeAction<object, ErrorEventArgs>(OnError, "LogStreamManager.OnError"));
        //        watcher.Start();
        //        _watcher = watcher;
        //    }

        //    if (_heartbeat == null)
        //    {
        //        _heartbeat = new Timer(OnHeartbeat, null, HeartbeatInterval, HeartbeatInterval);
        //    }
        //}

        // Suppress exception on callback to not crash the process.
        private Action<T1, T2> DoSafeAction<T1, T2>(Action<T1, T2> func, string eventName)
        {
            return (t1, t2) =>
            {
                try
                {
                    try
                    {
                        func(t1, t2);
                    }
                    catch (Exception ex)
                    {
                        using (_tracer.Step(eventName))
                        {
                            _tracer.TraceError(ex);
                        }
                    }
                }
                catch
                {
                    // no-op
                }
            };
        }
    }
}