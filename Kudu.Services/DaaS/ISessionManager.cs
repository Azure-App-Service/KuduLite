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
        Task UpdateActiveSession(Session activeSesion);
    }
}