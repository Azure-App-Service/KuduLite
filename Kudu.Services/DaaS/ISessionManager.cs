using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kudu.Services.Performance
{
    /// <summary>
    /// 
    /// </summary>
    public interface ISessionManager
    {
        Task<string> SubmitNewSession(Session session);
        Task<IEnumerable<Session>> GetAllSessions();
        Task<Session> GetSession(string sessionId);
        Task<Session> GetActiveSession();
        Task MarkCurrentInstanceAsComplete(Session activeSession);
        Task MarkCurrentInstanceAsStarted(Session activeSession);
        Task AddLogsToActiveSession(Session activeSession, IEnumerable<LogFile> logFile);
        bool HasThisInstanceCollectedLogs(Session activeSession);
        bool AllInstancesCollectedLogs(Session activeSession);
        bool ShouldCollectOnCurrentInstance(Session activeSession);
        Task MarkSessionAsComplete(Session activeSession);
        string GetInstanceId();
    }
}