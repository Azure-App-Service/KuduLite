using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kudu.Contracts.Tracing;
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
        private readonly List<string> _allSessionsDirs = new List<string>()
        {
            SessionDirectories.ActiveSessionsDir,
            SessionDirectories.CompletedSessionsDir
        };

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tracer"></param>
        public SessionManager(ITracer tracer)
        {
            _tracer = tracer;
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
                foreach(var sessionFile in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
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
            session.Status = SessionStatus.Active;
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

        public async Task UpdateActiveSession(Session activeSesion)
        {
            await WriteJsonAsync(activeSesion,
                Path.Combine(SessionDirectories.ActiveSessionsDir, activeSesion.SessionId + ".json"));
        }
    }
}