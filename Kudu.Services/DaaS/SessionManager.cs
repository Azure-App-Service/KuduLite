using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Helpers;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Newtonsoft.Json;

namespace Kudu.Services.Performance
{
    /// <summary>
    /// 
    /// </summary>
    public class SessionManager : ISessionManager
    {
        const string SessionFileNameFormat = "yyMMdd_HHmmssffff";

        private readonly ITracer _tracer;
        private readonly ITraceFactory _traceFactory;
        private static IOperationLock _sessionLockFile;
        private readonly List<string> _allSessionsDirs = new List<string>()
        {
            SessionDirectories.ActiveSessionsDir,
            SessionDirectories.CompletedSessionsDir
        };

        /// <summary>
        /// 
        /// </summary>
        /// <param name="traceFactory"></param>
        /// <param name="environment"></param>
        public SessionManager(ITraceFactory traceFactory)
        {
            _traceFactory = traceFactory;
            _tracer = _traceFactory.GetTracer();

            CreateSessionDirectories();
        }

        private void CreateSessionDirectories()
        {
            _allSessionsDirs.ForEach(x =>
            {
                if (!Directory.Exists(x))
                    Directory.CreateDirectory(x);
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<Session> GetActiveSession()
        {
            var activeSessions = await LoadSessionsFromStorage(SessionDirectories.ActiveSessionsDir);
            return activeSessions.FirstOrDefault();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<Session>> GetAllSessions()
        {
            return (await LoadSessionsFromStorage(_allSessionsDirs));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        public async Task<Session> GetSession(string sessionId)
        {
            return (await LoadSessionsFromStorage(_allSessionsDirs))
                .Where(x => x.SessionId == sessionId).FirstOrDefault();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        public async Task<string> SubmitNewSession(Session session)
        {
            string sessionId = GetSessionId(session.StartTime);
            var existingSession = await GetSession(sessionId);

            if (existingSession != null)
            {
                //
                // An existing session with the same Id already
                // exists, just return the SessionId. This can 
                // happen if the call to submit the session went
                // to another instance.
                //

                return sessionId;
            }

            await SaveSession(session);
            return session.SessionId;
        }

        private async Task<List<Session>> LoadSessionsFromStorage(string directoryToLoadSessionsFrom)
        {
            return await LoadSessionsFromStorage(new List<string> { directoryToLoadSessionsFrom });
        }

        private async Task<List<Session>> LoadSessionsFromStorage(List<string> directoriesToLoadSessionsFrom)
        {
            var sessions = new List<Session>();

            foreach (var directory in directoriesToLoadSessionsFrom)
            {
                foreach (var sessionFile in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var session = await FromJsonFileAsync<Session>(sessionFile);
                        sessions.Add(session);
                    }
                    catch (Exception ex)
                    {
                        TraceExtensions.TraceError(_tracer, ex, "Failed while reading session", sessionFile);
                    }
                }
            }

            return sessions;
        }

        private async Task SaveSession(Session session)
        {
            session.StartTime = DateTime.UtcNow;
            session.SessionId = GetSessionId(session.StartTime);
            session.Status = Status.Active;
            await WriteJsonAsync(session,
                Path.Combine(SessionDirectories.ActiveSessionsDir, session.SessionId + ".json"));
        }

        private string GetSessionId(DateTime startTime)
        {
            return startTime.ToString(SessionFileNameFormat);
        }

        private async Task WriteJsonAsync(object objectToSerialize, string filePath)
        {
            await WriteTextAsync(filePath, JsonConvert.SerializeObject(objectToSerialize, Formatting.Indented));
        }

        private async Task<T> FromJsonFileAsync<T>(string filePath)
        {
            string fileContents = await ReadTextAsync(filePath);
            T obj = JsonConvert.DeserializeObject<T>(fileContents);
            return obj;
        }

        async Task<string> ReadTextAsync(string path)
        {
            var sb = new StringBuilder();
            using (var sourceStream = new FileStream(
             path,
             FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
             bufferSize: 4096, useAsync: true))
            {
                byte[] buffer = new byte[0x1000];
                int numRead;
                while ((numRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    string text = Encoding.Unicode.GetString(buffer, 0, numRead);
                    sb.Append(text);
                }

                return sb.ToString();
            }
        }

        async Task WriteTextAsync(string filePath, string text)
        {
            byte[] encodedText = Encoding.Unicode.GetBytes(text);

            using (var sourceStream =
                new FileStream(
                    filePath,
                    FileMode.Create, FileAccess.Write, FileShare.ReadWrite,
                    bufferSize: 4096, useAsync: true))
            {
                await sourceStream.WriteAsync(encodedText, 0, encodedText.Length);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="updatedSession"></param>
        /// <param name="sessionId"></param>
        /// <param name="callerMethodName"></param>
        /// <returns></returns>
        private async Task UpdateSession(Func<Session> updatedSession, string sessionId, [CallerMemberName] string callerMethodName = "")
        {
            try
            {
                _sessionLockFile = await AcquireSesssionLock(sessionId, callerMethodName);

                if (_sessionLockFile == null)
                {
                    //
                    // We failed to acquire the lock on the session file
                    //

                    return;
                }

                Session activeSession = updatedSession();
                await UpdateActiveSession(activeSession);

                if (_sessionLockFile != null)
                {
                    LogMessage($"SessionLock released by {callerMethodName} for {sessionId} on {System.Environment.MachineName}");
                    _sessionLockFile.Release();
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed while updating session - {sessionId}", ex);
            }
        }

        private async Task<IOperationLock> AcquireSesssionLock(string sessionId, string callerMethodName)
        {
            IOperationLock sessionLock;
            int loopCount = 0;

            if (OSDetector.IsOnWindows())
            {
                sessionLock = new LockFile(GetActiveSessionLockPath(sessionId), _traceFactory);
            }
            else
            {
                sessionLock = new LinuxLockFile(GetActiveSessionLockPath(sessionId), _traceFactory);
            }

            LogMessage($"{callerMethodName} going to SessionLock by for {sessionId} on {System.Environment.MachineName}");
            while (!sessionLock.Lock(callerMethodName) 
                && loopCount <= 60)
            {
                ++loopCount;
                LogMessage($"Waiting to acquire the lock on session file {sessionId} on {System.Environment.MachineName} invoked by {callerMethodName}");
                await Task.Delay(1000);
            }

            if (loopCount > 60)
            {
                LogMessage($"Deleting the lock file for {sessionId} as it seems to be in an orphaned stage");
                sessionLock.Release();
                return null;
            }

            return sessionLock;
        }

        private async Task UpdateActiveSession(Session activeSesion)
        {
            await WriteJsonAsync(activeSesion,
                Path.Combine(SessionDirectories.ActiveSessionsDir, activeSesion.SessionId + ".json"));
        }

        private string GetActiveSessionLockPath(string sessionId)
        {
            return Path.Combine(SessionDirectories.ActiveSessionsDir, sessionId + ".json.lock");
        }

        public async Task MarkCurrentInstanceAsComplete(Session activeSession)
        {
            await UpdateCurrentInstanceStatus(activeSession, Status.Complete);
        }

        public async Task MarkCurrentInstanceAsStarted(Session activeSession)
        {
            await UpdateCurrentInstanceStatus(activeSession, Status.Started);
        }

        public async Task UpdateCurrentInstanceStatus(Session activeSession, Status sessionStatus)
        {
            try
            {
                await UpdateSession(() =>
                {
                    if (activeSession.ActiveInstances == null)
                    {
                        activeSession.ActiveInstances = new List<ActiveInstance>();
                    }

                    var activeInstance = activeSession.ActiveInstances.FirstOrDefault(x => x.Name.Equals(GetInstanceId(), StringComparison.OrdinalIgnoreCase));
                    if (activeInstance == null)
                    {
                        activeInstance = new ActiveInstance(GetInstanceId());
                        activeSession.ActiveInstances.Add(activeInstance);
                    }

                    activeInstance.Status = sessionStatus;
                    return activeSession;
                }, activeSession.SessionId);
            }
            catch(Exception ex)
            {
                LogError($"Failed while updating current instance status to {sessionStatus}", ex);
            }
        }

        public async Task AddLogsToActiveSession(Session activeSession, IEnumerable<LogFile> logFiles)
        {
            try
            {
                foreach (var log in logFiles)
                {
                    log.Size = GetFileSize(log.FullPath);
                    log.Name = Path.GetFileName(log.FullPath);
                }

                await CopyLogsToPermanentLocation(logFiles, activeSession);

                LogMessage($"Logs copied to permanent storage for {activeSession.SessionId}");

                await UpdateSession(() =>
                {
                    try
                    {
                        LogMessage($"Adding active Instance added for {activeSession.SessionId}");

                        if (activeSession.ActiveInstances == null)
                        {
                            activeSession.ActiveInstances = new List<ActiveInstance>();
                        }

                        ActiveInstance activeInstance = activeSession.ActiveInstances.FirstOrDefault(x => x.Name.Equals(GetInstanceId(), StringComparison.OrdinalIgnoreCase));
                        if (activeInstance == null)
                        {
                            activeInstance = new ActiveInstance(GetInstanceId());
                            activeSession.ActiveInstances.Add(activeInstance);
                        }

                        LogMessage($"Active Instance added for {activeSession.SessionId}");

                        activeInstance.Logs.AddRange(logFiles);
                    }
                    catch (Exception ex)
                    {
                        LogError("Failed while adding active instance", ex);
                    }
                    return activeSession;
                }, activeSession.SessionId);
            }
            catch (Exception ex)
            {
                LogError("Failed in AddLogsToActiveSession", ex);
            }
        }

        private async Task CopyLogsToPermanentLocation(IEnumerable<LogFile> logFiles, Session activeSession)
        {
            try
            {
                foreach (var log in logFiles)
                {
                    string logPath = Path.Combine(
                        activeSession.SessionId,
                        $"{GetInstanceId()}_{Path.GetFileName(log.FullPath)}");

                    log.RelativePath = $"{System.Environment.GetEnvironmentVariable(Constants.HttpHost)}/api/vfs/{ConvertBackSlashesToForwardSlashes(logPath)}";
                    string destination = Path.Combine(LogsDirectories.LogsDir, logPath);
                    await CopyFileAsync(log.FullPath, destination);
                }
            }
            catch (Exception ex)
            {
                LogError("Failed while copying logs to permanent storage", ex);
            }
        }

        private long GetFileSize(string path)
        {
            return new FileInfo(path).Length;
        }

        private string ConvertBackSlashesToForwardSlashes(string logPath)
        {
            string relativePath = Path.Combine(LogsDirectories.LogsDirRelativePath, logPath);
            relativePath = relativePath.Replace('\\', '/');
            return relativePath.TrimStart('/');
        }

        // https://stackoverflow.com/questions/882686/non-blocking-file-copy-in-c-sharp
        private async Task CopyFileAsync(string sourceFile, string destinationFile)
        {
            try
            {
                CreateDirectoryIfNotExists(Path.GetDirectoryName(destinationFile));

                LogMessage($"Copying file from {sourceFile} to {destinationFile}");

                using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var destinationStream = new FileStream(destinationFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await sourceStream.CopyToAsync(destinationStream);

                LogMessage($"File copied from {sourceFile} to {destinationFile}");
            }
            catch(Exception ex)
            {
                LogError("Failed while copying logs", ex);
            }
        }

        private void CreateDirectoryIfNotExists(string directory)
        {
            if (!FileSystemHelpers.DirectoryExists(directory))
                FileSystemHelpers.CreateDirectory(directory);
        }

        public bool HasThisInstanceCollectedLogs(Session activeSession)
        {
            return activeSession.ActiveInstances != null
                && activeSession.ActiveInstances.Any(x => x.Name.Equals(GetInstanceId(),
                StringComparison.OrdinalIgnoreCase) && x.Status == Status.Complete);
        }

        public string GetInstanceId()
        {
            return System.Environment.MachineName;
        }

        public bool AllInstancesCollectedLogs(Session activeSession)
        {
            if (activeSession.ActiveInstances == null)
            {
                return false;
            }

            var completedInstances = activeSession.ActiveInstances.Where(x => x.Status == Status.Complete).Select(x => x.Name);
            return completedInstances.SequenceEqual(activeSession.Instances, StringComparer.OrdinalIgnoreCase);
        }

        public async Task MarkSessionAsComplete(Session activeSession)
        {
            await UpdateSession(() =>
            {
                activeSession.Status = Status.Complete;
                activeSession.EndTime = DateTime.UtcNow;
                return activeSession;

            }, activeSession.SessionId);

            string activeSessionFile = Path.Combine(SessionDirectories.ActiveSessionsDir, activeSession.SessionId + ".json");
            string completedSessionFile = Path.Combine(SessionDirectories.CompletedSessionsDir, activeSession.SessionId + ".json");

            FileSystemHelpers.MoveFile(activeSessionFile, completedSessionFile);

            TraceExtensions.Trace(_tracer, $"Session {activeSession.SessionId} is complete");
        }

        public bool ShouldCollectOnCurrentInstance(Session activeSession)
        {
            return activeSession.Instances != null &&
                activeSession.Instances.Any(x => x.Equals(GetInstanceId(), StringComparison.OrdinalIgnoreCase));
        }

        internal string GetTemporaryFolderPath()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "dotnet-monitor");
            LogMessage($"TempPath = {tempPath}");
            CreateDirectoryIfNotExists(tempPath);
            return tempPath;
        }

        public async Task RunToolForSessionAsync(Session activeSession, CancellationToken token)
        {
            LogMessage($"RunToolForSessionAsync {activeSession.SessionId}");

            IDiagnosticTool diagnosticTool = GetDiagnosticTool(activeSession);
            await MarkCurrentInstanceAsStarted(activeSession);
            string tempPath = GetTemporaryFolderPath();

            LogMessage($"Invoking Diagnostic tool for session {activeSession.SessionId}");
            var logs = await diagnosticTool.InvokeAsync(activeSession.ToolParams, tempPath);
            {
                await AddLogsToActiveSession(activeSession, logs);
            }

            await MarkCurrentInstanceAsComplete(activeSession);
            await CheckandCompleteSessionIfNeeded(activeSession);
        }

        public async Task<bool> CheckandCompleteSessionIfNeeded(Session activeSession, bool forceCompletion = false)
        {
            if (AllInstancesCollectedLogs(activeSession) || forceCompletion)
            {
                await MarkSessionAsComplete(activeSession);
                return true;
            }

            return false;
        }

        private static IDiagnosticTool GetDiagnosticTool(Session activeSession)
        {
            IDiagnosticTool diagnosticTool;
            if (activeSession.Tool == DiagnosticTool.MemoryDump)
            {
                diagnosticTool = new MemoryDumpTool();
            }
            else if (activeSession.Tool == DiagnosticTool.Profiler)
            {
                diagnosticTool = new ClrTraceTool();
            }
            else
            {
                throw new ApplicationException($"Diagnostic Tool of type {activeSession.Tool} not found");
            }

            return diagnosticTool;
        }

        private static void LogMessage(string message)
        {
            KuduEventGenerator.Log().GenericEvent(ServerConfiguration.GetApplicationName(),
                message,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        private static void LogError(string message, Exception ex)
        {
            KuduEventGenerator.Log().GenericEvent(ServerConfiguration.GetApplicationName(),
                message + " " + ex.ToString(),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
        }
    }
}