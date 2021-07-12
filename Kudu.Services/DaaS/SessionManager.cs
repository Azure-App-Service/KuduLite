using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Tracing;
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
        /// <param name="tracer"></param>
        /// <param name="traceFactory"></param>
        public SessionManager(ITracer tracer, ITraceFactory traceFactory)
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
            return (await LoadSessionsFromStorage(SessionDirectories.ActiveSessionsDir))
                .FirstOrDefault();
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
             FileMode.Open, FileAccess.Read, FileShare.Read,
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
        /// <returns></returns>
        private async Task UpdateSession(Func<Session> updatedSession, string sessionId)
        {
            if (OSDetector.IsOnWindows())
            {
                _sessionLockFile = new LockFile(GetActiveSessionLockPath(sessionId), _traceFactory);
            }
            else
            {
                _sessionLockFile = new LinuxLockFile(GetActiveSessionLockPath(sessionId), _traceFactory);
            }

            if (_sessionLockFile.Lock("AcquireSessionLock"))
            {
                Session activeSession = updatedSession();
                await UpdateActiveSession(activeSession);
            }

            if (_sessionLockFile != null)
            {
                _sessionLockFile.Release();
            }
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
            await UpdateCurrentInstanceStatus(activeSession, Status.Completed);
        }

        public async Task MarkCurrentInstanceAsStarted(Session activeSession)
        {
            await UpdateCurrentInstanceStatus(activeSession, Status.Started);
        }

        public async Task UpdateCurrentInstanceStatus(Session activeSession, Status sessionStatus)
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

        public async Task AddLogsToActiveSession(Session activeSession, IEnumerable<LogFile> logFiles)
        {
            await UpdateSession(() =>
            {
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

                activeInstance.Logs.AddRange(logFiles);
                return activeSession;
            }, activeSession.SessionId);
        }

        public bool HasThisInstanceCollectedLogs(Session activeSession)
        {
            return activeSession.ActiveInstances != null
                && activeSession.ActiveInstances.Any(x => x.Name.Equals(GetInstanceId(),
                StringComparison.OrdinalIgnoreCase) && x.Status == Status.Completed);
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

            var completedInstances = activeSession.ActiveInstances.Where(x => x.Status == Status.Completed).Select(x => x.Name);
            return completedInstances.SequenceEqual(activeSession.Instances, StringComparer.OrdinalIgnoreCase);
        }

        public async Task MarkSessionAsComplete(Session activeSession)
        {
            await UpdateSession(() =>
            {
                activeSession.Status = Status.Completed;
                activeSession.EndTime = DateTime.UtcNow;
                return activeSession;

            }, activeSession.SessionId);

            string activeSessionFile = Path.Combine(SessionDirectories.ActiveSessionsDir, activeSession.SessionId + ".json");
            string completedSessionFile = Path.Combine(SessionDirectories.CompletedSessionsDir, activeSession.SessionId + ".json");

            FileSystemHelpers.MoveFile(activeSessionFile, completedSessionFile);
        }

        public bool ShouldCollectOnCurrentInstance(Session activeSession)
        {
            return activeSession.Instances != null &&
                activeSession.Instances.Any(x => x.Equals(GetInstanceId(), StringComparison.OrdinalIgnoreCase));
        }
    }
}