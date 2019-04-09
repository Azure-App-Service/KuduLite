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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;

namespace Kudu.Services.Performance
{
    public class LogStreamManager
    {
        private const string FilterQueryKey = "filter";
        private const string AzureDriveEnabledKey = "AzureDriveEnabled";

        private static string[] LogFileExtensions = new string[] { ".txt", ".log", ".htm" };

        // Azure 3 mins timeout, heartbeat every mins keep alive.
        private static TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);

        private readonly object _thisLock = new object();
        private readonly string _logPath;
        private string _filter;
        private readonly IEnvironment _environment;
        private readonly ITracer _tracer;
        private readonly IOperationLock _operationLock;
        // CORE TODO No longer needed, each task can check context.RequestAborted token
        // private readonly List<Task> _results;

        private Dictionary<string, long> _logFiles;
        private IFileSystemWatcher _watcher;
        private Timer _heartbeat;
        private DateTime _lastTraceTime;
        private DateTime _startTime;
        private TimeSpan _timeout;

        private const string volatileLogsPath = "/appsvctmp/volatile/logs/runtime";

        // CORE TODO
        //private ShutdownDetector _shutdownDetector;
        //private CancellationTokenRegistration _cancellationTokenRegistration;

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

            DisableResponseBuffering(context);
            var stopwatch = Stopwatch.StartNew();
            
            // CORE TODO Shutdown detector registration

            // CORE TODO double check on semantics of this (null vs empty etc);
            _filter = context.Request.Query[FilterQueryKey].ToString();

            // CORE TODO parse from path
            // path route as in logstream/{*path} without query strings
            // string routePath = context.Request.RequestContext.RouteData.Values["path"] as string;

            string path;
            string routePath = "";
            //bool enableTrace;

            // trim '/'
            routePath = String.IsNullOrEmpty(routePath) ? routePath : routePath.Trim('/');

            var firstPath = routePath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            // Ensure mounted logFiles dir 

            string mountedLogFilesDir = Path.Combine(_logPath, routePath);

            FileSystemHelpers.EnsureDirectory(mountedLogFilesDir);

            if (shouldMonitiorMountedLogsPath(mountedLogFilesDir))
            {
                path = mountedLogFilesDir;
            }
            else
            {
                path = volatileLogsPath;
            }

            context.Response.Headers.Add("Content-Type", "text/event-stream");

            await WriteInitialMessage(context);

            // CORE TODO Get the fsw and keep it in scope here with a using that ends at the end

            lock (_thisLock)
            {
                Initialize(path, context);
            }

            if (_logFiles != null)
            {
                foreach (string log in _logFiles.Keys)
                {
                    var reader = new StreamReader(log, Encoding.ASCII);
                    foreach (string logLine in Tail(reader, 10))
                    {
                        await context.Response.WriteAsync(
                            string.Format(System.Environment.NewLine, 
                                logLine, 
                                System.Environment.NewLine));
                    }
                }
            }
            else
            {
                _tracer.TraceError("LogStream: No pervious logfiles");
                Console.WriteLine("LogStream: No pervious logfiles");
            }

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

        ///<summary>Returns the end of a text reader.</summary>
        ///<param name="reader">The reader to read from.</param>
        ///<param name="lineCount">The number of lines to return.</param>
        ///<returns>The last lineCount lines from the reader.</returns>
        public string[] Tail(TextReader reader, int lineCount)
        {
            var buffer = new List<string>(lineCount);
            string line;
            for (int i = 0; i < lineCount; i++)
            {
                line = reader.ReadLine();
                if (line == null) return buffer.ToArray();
                buffer.Add(line);
            }

            int lastLine = lineCount - 1;           //The index of the last line read from the buffer.  Everything > this index was read earlier than everything <= this indes

            while (null != (line = reader.ReadLine()))
            {
                lastLine++;
                if (lastLine == lineCount) lastLine = 0;
                buffer[lastLine] = line;
            }

            if (lastLine == lineCount - 1) return buffer.ToArray();
            var retVal = new string[lineCount];
            buffer.CopyTo(lastLine + 1, retVal, 0, lineCount - lastLine - 1);
            buffer.CopyTo(0, retVal, lineCount - lastLine - 1, lastLine + 1);
            return retVal;
        }

        private static Task WriteInitialMessage(HttpContext context)
        {
            var msg = String.Format( 
                CultureInfo.CurrentCulture, 
                Resources.LogStream_Welcome, 
                DateTime.UtcNow.ToString("s"), 
                System.Environment.NewLine);

            return context.Response.WriteAsync(msg);
        }
        
        /// <summary>
        /// Determines if Kudu Should Monitor Mounted Logs directory,
        /// or the mounted fs logs dir, if kudu
        /// </summary>
        /// <returns></returns>
        private static bool shouldMonitiorMountedLogsPath(string mountedDirPath)
        {
            int count = 0;
            string dateToday = DateTime.Now.ToString("yyyy_MM_dd");

            if (FileSystemHelpers.DirectoryExists(mountedDirPath))
            {
                // if more than two log files present that are generated today, 
                // use this directory; first file for a date is the marker file
                foreach (var file in Directory.GetFiles(mountedDirPath, "*", SearchOption.AllDirectories))
                {
                    if (file.StartsWith(dateToday) && ++count > 1)
                    {
                        break;
                    }
                }
            }
            return count==2;
        }

        private void Initialize(string path, HttpContext context)
        {
            System.Diagnostics.Debug.Assert(_watcher == null, "we only allow one manager per request!");

            // initalize _logFiles before the file watcher since file watcher event handlers reference _logFiles
            // this mirrors the Reset() where we stop the file watcher before nulling _logFile.
            if (_logFiles == null)
            {
                var logFiles = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var ext in LogFileExtensions)
                {
                    foreach (var file in Directory.GetFiles(path, "*" + ext, SearchOption.AllDirectories))
                    {
                        try
                        {
                            logFiles[file] = new FileInfo(file).Length;
                        }
                        catch (Exception ex)
                        {
                            // avoiding racy with providers cleaning up log file
                            _tracer.TraceError(ex);
                        }
                    }
                }

                _logFiles = logFiles;
            }

            if (_watcher == null)
            {
                IFileSystemWatcher watcher = OSDetector.IsOnWindows()
                    ? (IFileSystemWatcher)new FileSystemWatcherWrapper(path, includeSubdirectories: true)
                    : new NaiveFileSystemWatcher(path, LogFileExtensions);
                watcher.Changed += new FileSystemEventHandler(DoSafeAction<object, FileSystemEventArgs, HttpContext>(OnChanged, "LogStreamManager.OnChanged", context));
                watcher.Deleted += new FileSystemEventHandler(DoSafeAction<object, FileSystemEventArgs,HttpContext>(OnDeleted, "LogStreamManager.OnDeleted", context));
                watcher.Renamed += new RenamedEventHandler(DoSafeAction<object, RenamedEventArgs,HttpContext>(OnRenamed, "LogStreamManager.OnRenamed", context));
                //watcher.Error += new ErrorEventHandler(DoSafeAction<object, ErrorEventArgs,HttpContext>(OnError, "LogStreamManager.OnError", context));
                watcher.Error += new ErrorEventHandler(DoSafeAction<object, ErrorEventArgs,HttpContext>(OnError, "LogStreamManager.OnError", context));
                watcher.Start();
                _watcher = watcher;
            }

            if (_heartbeat == null)
            {
                _heartbeat = new Timer(OnHeartbeat, context, HeartbeatInterval, HeartbeatInterval);
            }
        }

        private Action<T1> HeartbeatWrapper<T1, T2>(Action<T1,T2> func, T2 context)
        {
            return (t1) =>
            {
                try
                {
                    try
                    {
                        func(t1, context);
                    }
                    catch (Exception ex)
                    {
                        using (_tracer.Step("LogStreamManager.Heartbeat"))
                        {
                            _tracer.TraceError(ex);
                        }
                    }

                }
                catch
                {
                    //no-op 
                }
            };

        }

        // Suppress exception on callback to not crash the process.
        private Action<T1, T2> DoSafeAction<T1, T2, T3>(Action<T1, T2, T3> func, string eventName, T3 context)
        {
            return (t1, t2) =>
            {
                try
                {
                    try
                    {
                        func(t1, t2, context);
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
        
        private void DisableResponseBuffering(HttpContext context)
        {
            IHttpBufferingFeature bufferingFeature = context.Features.Get<IHttpBufferingFeature>();
            if (bufferingFeature != null)
            {
                bufferingFeature.DisableResponseBuffering();
            }
        }
        
        private void Reset()
        {
            if (_watcher != null)
            {
                _watcher.Stop();
                // dispose is blocked till all change request handled, 
                // this could lead to deadlock as we share the same lock
                // http://stackoverflow.com/questions/73128/filesystemwatcher-dispose-call-hangs
                // in the meantime, let GC handle it
                // _watcher.Dispose();
                _watcher = null;
            }

            if (_heartbeat != null)
            {
                _heartbeat.Dispose();
                _heartbeat = null;
            }

            _logFiles = null;
        }
        
        private void OnHeartbeat(object state)
        {
            try
            {
                try
                {
                    HttpContext context = (HttpContext) state;
                    TimeSpan ts = DateTime.UtcNow.Subtract(_startTime);
                    if (ts >= _timeout)
                    {
                        TerminateClient(String.Format(CultureInfo.CurrentCulture, Resources.LogStream_Timeout, DateTime.UtcNow.ToString("s"), (int)ts.TotalMinutes, System.Environment.NewLine), context);
                    }
                    else
                    {
                        ts = DateTime.UtcNow.Subtract(_lastTraceTime);
                        if (ts >= HeartbeatInterval)
                        {
                            NotifyClient(String.Format(CultureInfo.CurrentCulture, Resources.LogStream_Heartbeat, DateTime.UtcNow.ToString("s"), (int)ts.TotalMinutes, System.Environment.NewLine),context);
                        }
                    }
                }
                catch (Exception ex)
                {
                    using (_tracer.Step("LogStreamManager.OnHeartbeat"))
                    {
                        _tracer.TraceError(ex);
                    }
                }
            }
            catch
            {
                // no-op
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e, HttpContext context)
        {
            if (e.ChangeType == WatcherChangeTypes.Changed && MatchFilters(e.FullPath))
            {
                // reading the delta of file changed, retry if failed.
                IEnumerable<string> lines = null;
                OperationManager.Attempt(() =>
                {
                    lines = GetChanges(e);
                }, 3, 100);

                if (lines.Count() > 0)
                {
                    _lastTraceTime = DateTime.UtcNow;
                    NotifyClient(lines,context);
                }
            }
        }

        /*
        private string ParseRequest(HttpContext context)
        {
            _filter = context.Request.QueryString[FilterQueryKey];

            // path route as in logstream/{*path} without query strings
            string routePath = context.Request.RequestContext.RouteData.Values["path"] as string;
            
            // trim '/'
            routePath = String.IsNullOrEmpty(routePath) ? routePath : routePath.Trim('/');

            // logstream at root
            if (String.IsNullOrEmpty(routePath))
            {
                _enableTrace = true;
                return _logPath;
            }

            var firstPath = routePath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.Equals(firstPath, "Application", StringComparison.OrdinalIgnoreCase))
            {
                _enableTrace = true;
            }

            return FileSystemHelpers.EnsureDirectory(Path.Combine(_logPath, routePath));
        }
        */

        private static bool MatchFilters(string fileName)
        {
            if (!string.IsNullOrEmpty(fileName))
            {
                foreach (string ext in LogFileExtensions)
                {
                    if (fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void NotifyClient(string text, HttpContext context)
        {
            NotifyClient(new string[] { text },context);
        }

        private async void NotifyClient(IEnumerable<string> lines, HttpContext context)
        {
                if (!context.RequestAborted.IsCancellationRequested)
                {
                    try
                    {
                        foreach (var line in lines)
                        {
                            await context.Response.WriteAsync(line);
                        }
                    }
                    catch (Exception)
                    {
                        _tracer.TraceError("Error notifying client");
                    }
                }
        }

        private IEnumerable<string> GetChanges(FileSystemEventArgs e)
        {
            lock (_thisLock)
            {
                // do no-op if races between idle timeout and file change event
                /*
                if (_results.Count == 0)
                {
                    return Enumerable.Empty<string>();
                }
                */

                long offset = 0;
                if (!_logFiles.TryGetValue(e.FullPath, out offset))
                {
                    _logFiles[e.FullPath] = 0;
                }

                using (FileStream fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long length = fs.Length;

                    // file was truncated
                    if (offset > length)
                    {
                        _logFiles[e.FullPath] = offset = 0;
                    }

                    // multiple events
                    if (offset == length)
                    {
                        return Enumerable.Empty<string>();
                    }

                    if (offset != 0)
                    {
                        fs.Seek(offset, SeekOrigin.Begin);
                    }

                    List<string> changes = new List<string>();

                    StreamReader reader = new StreamReader(fs);
                    while (!reader.EndOfStream)
                    {
                        string line = ReadLine(reader);
                        if (String.IsNullOrEmpty(_filter) || line.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            changes.Add(line);
                        }
                    }

                    // Adjust offset and return changes
                    _logFiles[e.FullPath] = reader.BaseStream.Position;

                    return changes;
                }
            }
        }

        private void OnDeleted(object sender, FileSystemEventArgs e, HttpContext context)
        {
            if (e.ChangeType == WatcherChangeTypes.Deleted)
            {
                lock (_thisLock)
                {
                    _logFiles.Remove(e.FullPath);
                }
            }
        }

        private void OnRenamed(object sender, RenamedEventArgs e, HttpContext context)
        {
            if (e.ChangeType == WatcherChangeTypes.Renamed)
            {
                lock (_thisLock)
                {
                    _logFiles.Remove(e.OldFullPath);
                }
            }
        }

        private void OnError(object sender, ErrorEventArgs e, HttpContext context)
        {
            try
            {
                lock (_thisLock)
                {
                    if (_watcher != null)
                    {
                        string path = _watcher.Path;
                        Reset();
                        Initialize(path,context);
                    }
                }
            }
            catch (Exception ex)
            {
                OnCriticalError(ex, context);
            }
        }

        private void OnCriticalError(Exception ex, HttpContext context)
        {
            TerminateClient(String.Format(CultureInfo.CurrentCulture, Resources.LogStream_Error, System.Environment.NewLine, DateTime.UtcNow.ToString("s"), ex.Message), context);
        }

        private void TerminateClient(string text, HttpContext context)
        {
            NotifyClient(text, context);
            lock (_thisLock)
            {
                // Proactively cleanup resources
                Reset();
            }
            /*
            lock (_thisLock)
            {
                foreach (ProcessRequestAsyncResult result in _results)
                {
                    //CORE CHECK
                    result.Complete(false);
                }

                _results.Clear();

                // Proactively cleanup resources
                Reset();
            }
            */
        }

        // this has the same performance and implementation as StreamReader.ReadLine()
        // they both account for '\n' or '\r\n' as new line chars.  the difference is 
        // this returns the result with preserved new line chars.
        // without this, logstream can only guess whether it is '\n' or '\r\n' which is 
        // subjective to each log providers/files.
        private static string ReadLine(StreamReader reader)
        {
            var strb = new StringBuilder();
            int val;
            while ((val = reader.Read()) >= 0)
            {
                char ch = (char)val;
                strb.Append(ch);
                switch (ch)
                {
                    case '\r':
                    case '\n':
                        if (ch == '\r' && (char)reader.Peek() == '\n')
                        {
                            ch = (char)reader.Read();
                            strb.Append(ch);
                        }
                        return strb.ToString();
                    default:
                        break;
                }
            }

            return strb.ToString();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Reset();
            }
        }
    }
}
    
    
