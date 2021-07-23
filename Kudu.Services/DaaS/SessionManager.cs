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
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Newtonsoft.Json;

namespace Kudu.Services.DaaS
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
        /// SessionManager constructor 
        /// </summary>
        /// <param name="traceFactory"></param>
        public SessionManager(ITraceFactory traceFactory)
        {
            _traceFactory = traceFactory;
            _tracer = _traceFactory.GetTracer();

            CreateSessionDirectories();
        }

        #region ISessionManager methods

        /// <summary>
        /// ISessionManager - GetActiveSessionAsync
        /// </summary>
        /// <returns></returns>
        public async Task<Session> GetActiveSessionAsync()
        {
            var activeSessions = await LoadSessionsAsync(SessionDirectories.ActiveSessionsDir);
            return activeSessions.FirstOrDefault();
        }

        /// <summary>
        /// ISessionManager - GetAllSessionsAsync
        /// </summary>
        /// <returns></returns>
        public async Task<IEnumerable<Session>> GetAllSessionsAsync()
        {
            return (await LoadSessionsAsync(_allSessionsDirs)).OrderByDescending(x => x.StartTime);
        }

        /// <summary>
        /// ISessionManager - GetSessionAsync
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        public async Task<Session> GetSessionAsync(string sessionId)
        {
            return (await LoadSessionsAsync(_allSessionsDirs))
                .Where(x => x.SessionId == sessionId).FirstOrDefault();
        }

        /// <summary>
        /// ISessionManager - SubmitNewSessionAsync
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        public async Task<string> SubmitNewSessionAsync(Session session)
        {
            var activeSession = await GetActiveSessionAsync();
            if (activeSession != null)
            {
                throw new AccessViolationException("There is an already an existing active session");
            }

            if (session.Tool == DiagnosticTool.Unspecified)
            {
                throw new ArgumentException("Please specify a diagnostic tool");
            }

            await SaveSessionAsync(session);
            return session.SessionId;
        }

        /// <summary>
        /// ISessionManager - HasThisInstanceCollectedLogs
        /// </summary>
        /// <returns></returns>
        public async Task<bool> HasThisInstanceCollectedLogs()
        {
            var activeSession = await GetActiveSessionAsync();
            return activeSession.ActiveInstances != null
                && activeSession.ActiveInstances.Any(x => x.Name.Equals(GetInstanceId(),
                StringComparison.OrdinalIgnoreCase) && x.Status == Status.Complete);
        }

        /// <summary>
        /// ISessionManager - RunToolForSessionAsync
        /// </summary>
        /// <param name="activeSession"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task RunToolForSessionAsync(Session activeSession, CancellationToken token)
        {
            try
            {
                DiagnosticToolResponse resp = null;
                IDiagnosticTool diagnosticTool = GetDiagnosticTool(activeSession);
                await SetCurrentInstanceAsStartedAsync(activeSession);

                try
                {
                    resp = await diagnosticTool.InvokeAsync(activeSession.SessionId,
                    activeSession.ToolParams,
                    GetTemporaryFolderPath(),
                    GetInstanceIdShort(),
                    token);
                }
                catch (Exception ex)
                {
                    resp = new DiagnosticToolResponse();
                    resp.Errors.Add($"Invoking diagnostic tool failed with error - {ex.Message}");
                    DaasLogger.LogSessionError("Tool invocation failed", activeSession.SessionId, ex);
                }

                //
                // Add the tool output to the active session
                //
                await AppendToolResponseToSessionAsync(activeSession, resp);

                //
                // Mark current instance as Complete
                //
                await SetCurrentInstanceAsCompleteAsync(activeSession);

                //
                // Check if all the instances have finished running the session
                // and set the Session State to Complete
                //
                await CheckandCompleteSessionIfNeededAsync();
            }
            catch (Exception ex)
            {
                DaasLogger.LogSessionError("Exception while running tool", activeSession.SessionId, ex);
            }
        }

        /// <summary>
        /// ISessionManager - CheckandCompleteSessionIfNeededAsync
        /// </summary>
        /// <param name="forceCompletion"></param>
        /// <returns></returns>
        public async Task<bool> CheckandCompleteSessionIfNeededAsync(bool forceCompletion = false)
        {
            var activeSession = await GetActiveSessionAsync();
            if (AllInstancesCollectedLogs(activeSession) || forceCompletion)
            {
                DaasLogger.LogSessionMessage("All instances have status as Complete", activeSession.SessionId);
                await MarkSessionAsCompleteAsync(activeSession, forceCompletion: forceCompletion);
                return true;
            }

            return false;
        }

        /// <summary>
        /// ISessionManager - ShouldCollectOnCurrentInstance
        /// </summary>
        /// <param name="activeSession"></param>
        /// <returns></returns>
        public bool ShouldCollectOnCurrentInstance(Session activeSession)
        {
            return activeSession.Instances != null &&
                activeSession.Instances.Any(x => x.Equals(GetInstanceId(), StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        /// <summary>
        /// This method will copy the logs generated by the diagnostic session
        /// to permanent storage folder. This will also delete the log file
        /// generated in the temporary folder and append ShortInstanceId to the file
        /// name so it becomes easy to distinguish files from multiple instances in
        /// same session file. This method also updates the session after the files
        /// have been copied to permanent storage
        /// </summary>
        /// <param name="activeSession"></param>
        /// <param name="response"></param>
        /// <returns></returns>
        private async Task AppendToolResponseToSessionAsync(Session activeSession, DiagnosticToolResponse response)
        {
            try
            {
                foreach (var log in response.Logs)
                {
                    log.Size = GetFileSize(log.FullPath);
                    log.Name = Path.GetFileName(log.FullPath);
                }

                await CopyLogsToPermanentLocationAsync(response.Logs, activeSession);

                DaasLogger.LogSessionMessage($"Copied {response.Logs.Count()} logs to permanent storage", activeSession.SessionId);

                await UpdateActiveSessionAsync((latestSessionFromDisk) =>
                {
                    try
                    {
                        if (latestSessionFromDisk.ActiveInstances == null)
                        {
                            latestSessionFromDisk.ActiveInstances = new List<ActiveInstance>();
                        }

                        ActiveInstance activeInstance = latestSessionFromDisk.ActiveInstances.FirstOrDefault(x => x.Name.Equals(GetInstanceId(), StringComparison.OrdinalIgnoreCase));
                        if (activeInstance == null)
                        {
                            activeInstance = new ActiveInstance(GetInstanceId());
                            latestSessionFromDisk.ActiveInstances.Add(activeInstance);
                            DaasLogger.LogSessionMessage($"Added ActiveInstance to session", latestSessionFromDisk.SessionId);
                        }

                        activeInstance.Logs.AddRange(response.Logs);

                        if (response.Errors.Any())
                        {
                            activeInstance.Errors.AddRange(response.Errors);
                        }
                    }
                    catch (Exception ex)
                    {
                        DaasLogger.LogSessionError("Failed while adding active instance", latestSessionFromDisk.SessionId, ex);
                    }

                    return latestSessionFromDisk;
                }, activeSession.SessionId);
            }
            catch (Exception ex)
            {
                DaasLogger.LogSessionError("Failed in AddLogsToActiveSession", activeSession.SessionId, ex);
            }
        }

        private async Task UpdateActiveSessionAsync(Func<Session, Session> updateSession, string sessionId, [CallerMemberName] string callerMethodName = "")
        {
            try
            {
                _sessionLockFile = await AcquireSessionLockAsync(sessionId, callerMethodName);

                if (_sessionLockFile == null)
                {
                    //
                    // We failed to acquire the lock on the session file
                    //

                    return;
                }

                //
                // Load the latest session from the disk under the lock. This
                // ensures that only one instance can write to the sesion
                // and others wait while this instance has the lock
                //

                Session latestSessionFromDisk = await GetActiveSessionAsync();

                Session sessionAfterMergingLatestUpdates = updateSession(latestSessionFromDisk);
                await UpdateActiveSessionFileAsync(sessionAfterMergingLatestUpdates);
            }
            catch (Exception ex)
            {
                DaasLogger.LogSessionError($"Failed while updating session", sessionId, ex);
            }

            if (_sessionLockFile != null)
            {
                DaasLogger.LogSessionMessage($"SessionLock released by {callerMethodName}", sessionId);
                _sessionLockFile.Release();
            }
        }

        private async Task<List<Session>> LoadSessionsAsync(string directoryToLoadSessionsFrom)
        {
            return await LoadSessionsAsync(new List<string> { directoryToLoadSessionsFrom });
        }

        private async Task<List<Session>> LoadSessionsAsync(List<string> directoriesToLoadSessionsFrom)
        {
            var sessions = new List<Session>();
            foreach (var directory in directoriesToLoadSessionsFrom)
            {
                foreach (var sessionFile in FileSystemHelpers.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
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

        private async Task SaveSessionAsync(Session session)
        {
            try
            {
                session.StartTime = DateTime.UtcNow;
                session.SessionId = GetSessionId(session.StartTime);
                session.Status = Status.Active;
                await WriteJsonAsync(session,
                    Path.Combine(SessionDirectories.ActiveSessionsDir, session.SessionId + ".json"));

                var sessionInfo = new
                {
                    session.SessionId,
                    session.Instances,
                    session.Tool,
                    session.ToolParams,
                };

                DaasLogger.LogSessionMessage($"New session started {JsonConvert.SerializeObject(sessionInfo)}", session.SessionId);
            }
            catch (Exception ex)
            {
                DaasLogger.LogSessionError("Failed while saving the session", session.SessionId, ex);
            }
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
            using (var sourceStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
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
                new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, bufferSize: 4096,
                useAsync: true))
            {
                await sourceStream.WriteAsync(encodedText, 0, encodedText.Length);
            }
        }

        private async Task<IOperationLock> AcquireSessionLockAsync(string sessionId, string callerMethodName)
        {
            IOperationLock sessionLock = new SessionLockFile(GetActiveSessionLockPath(sessionId), _traceFactory);
            int loopCount = 0;

            DaasLogger.LogSessionMessage($"Acquiring SessionLock by {callerMethodName}", sessionId);
            while (!sessionLock.Lock(callerMethodName)
                && loopCount <= 60)
            {
                ++loopCount;
                await Task.Delay(1000);
            }

            if (loopCount > 60)
            {
                DaasLogger.LogSessionMessage($"Deleting the lock file as it seems to be in an orphaned stage", sessionId);
                sessionLock.Release();
                return null;
            }

            DaasLogger.LogSessionMessage($"Acquired SessionLock by {callerMethodName}", sessionId);
            return sessionLock;
        }

        private async Task UpdateActiveSessionFileAsync(Session activeSesion)
        {
            await WriteJsonAsync(activeSesion,
                Path.Combine(SessionDirectories.ActiveSessionsDir, activeSesion.SessionId + ".json"));
        }

        private string GetActiveSessionLockPath(string sessionId)
        {
            return Path.Combine(SessionDirectories.ActiveSessionsDir, sessionId + ".json.lock");
        }

        private async Task SetCurrentInstanceAsCompleteAsync(Session activeSession)
        {
            DaasLogger.LogSessionMessage("Setting current instance as Complete", activeSession.SessionId);
            await SetCurrentInstanceStatusAsync(activeSession, Status.Complete);
        }

        private async Task SetCurrentInstanceAsStartedAsync(Session activeSession)
        {
            DaasLogger.LogSessionMessage("Setting current instance as Started", activeSession.SessionId);
            await SetCurrentInstanceStatusAsync(activeSession, Status.Started);
        }

        private async Task SetCurrentInstanceStatusAsync(Session activeSession, Status sessionStatus)
        {
            try
            {
                await UpdateActiveSessionAsync((latestSessionFromDisk) =>
                {
                    if (latestSessionFromDisk.ActiveInstances == null)
                    {
                        latestSessionFromDisk.ActiveInstances = new List<ActiveInstance>();
                    }

                    var activeInstance = latestSessionFromDisk.ActiveInstances.FirstOrDefault(x => x.Name.Equals(GetInstanceId(), StringComparison.OrdinalIgnoreCase));
                    if (activeInstance == null)
                    {
                        activeInstance = new ActiveInstance(GetInstanceId());
                        latestSessionFromDisk.ActiveInstances.Add(activeInstance);
                    }

                    activeInstance.Status = sessionStatus;
                    return latestSessionFromDisk;
                }, activeSession.SessionId);
            }
            catch (Exception ex)
            {
                DaasLogger.LogSessionError($"Failed while updating current instance status to {sessionStatus}", activeSession.SessionId, ex);
            }
        }

        private async Task CopyLogsToPermanentLocationAsync(IEnumerable<LogFile> logFiles, Session activeSession)
        {
            foreach (var log in logFiles)
            {
                string logPath = Path.Combine(
                    activeSession.SessionId,
                    Path.GetFileName(log.FullPath));

                log.RelativePath = $"https://{System.Environment.GetEnvironmentVariable(Constants.HttpHost)}/api/vfs/{ConvertBackSlashesToForwardSlashes(logPath)}";
                string destination = Path.Combine(LogsDirectories.LogsDir, logPath);

                try
                {
                    await MoveFileAsync(log.FullPath, destination, activeSession.SessionId);
                }
                catch (Exception ex)
                {
                    DaasLogger.LogSessionError($"Failed while copying {logPath} to permanent storage", activeSession.SessionId, ex);
                }
            }
        }

        private bool GetComputerNameIfExists(out string computerName)
        {
            computerName = string.Empty;
            var env = Environment.GetEnvironmentVariable("COMPUTERNAME");
            if (!string.IsNullOrWhiteSpace(env))
            {
                computerName = env;
                return true;
            }

            return false;
        }

        private string GetInstanceId()
        {
            if (GetComputerNameIfExists(out string machineName))
            {
                return machineName;
            }

            return InstanceIdUtility.GetInstanceId();
        }

        private string GetInstanceIdShort()
        {
            if (GetComputerNameIfExists(out string machineName))
            {
                return machineName;
            }

            return InstanceIdUtility.GetShortInstanceId();
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
        private async Task MoveFileAsync(string sourceFile, string destinationFile, string sessionId)
        {
            try
            {
                FileSystemHelpers.EnsureDirectory(Path.GetDirectoryName(destinationFile));
                DaasLogger.LogSessionMessage($"Copying file from {sourceFile} to {destinationFile}", sessionId);

                using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    using (var destinationStream = new FileStream(destinationFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await sourceStream.CopyToAsync(destinationStream);
                    }
                }

                DaasLogger.LogSessionMessage($"File copied from {sourceFile} to {destinationFile}", sessionId);
                FileSystemHelpers.DeleteFileSafe(sourceFile);
            }
            catch (Exception ex)
            {
                DaasLogger.LogSessionError("Failed while copying logs", sessionId, ex);
            }
        }

        private void CreateSessionDirectories()
        {
            _allSessionsDirs.ForEach(x =>
            {
                FileSystemHelpers.EnsureDirectory(x);
            });
        }

        private bool AllInstancesCollectedLogs(Session activeSession)
        {
            if (activeSession.ActiveInstances == null)
            {
                return false;
            }

            var completedInstances = activeSession.ActiveInstances.Where(x => x.Status == Status.Complete).Select(x => x.Name);

            return Enumerable.SequenceEqual(completedInstances.OrderBy(x => x),
                activeSession.Instances.OrderBy(x => x),
                StringComparer.OrdinalIgnoreCase);
        }

        private async Task MarkSessionAsCompleteAsync(Session activeSession, bool forceCompletion = false)
        {
            await UpdateActiveSessionAsync((latestSessionFromDisk) =>
            {
                latestSessionFromDisk.Status = forceCompletion ? Status.TimedOut : Status.Complete;
                latestSessionFromDisk.EndTime = DateTime.UtcNow;
                return latestSessionFromDisk;

            }, activeSession.SessionId);

            string activeSessionFile = Path.Combine(SessionDirectories.ActiveSessionsDir, activeSession.SessionId + ".json");
            string completedSessionFile = Path.Combine(SessionDirectories.CompletedSessionsDir, activeSession.SessionId + ".json");

            //
            // Move the session file from Active to Complete folder
            //

            FileSystemHelpers.MoveFile(activeSessionFile, completedSessionFile);

            //
            // Clean-up the lock file from the Active session folder
            //

            FileSystemHelpers.DeleteFileSafe(GetActiveSessionLockPath(activeSession.SessionId));
            DaasLogger.LogSessionMessage($"Session is complete", activeSession.SessionId);
        }

        private string GetTemporaryFolderPath()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "dotnet-monitor");
            FileSystemHelpers.EnsureDirectory(tempPath);
            return tempPath;
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
    }
}