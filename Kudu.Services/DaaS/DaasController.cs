using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Kudu.Services.Performance
{
    /// <summary>
    /// 
    /// </summary>
    public class DaasController : Controller
    {
        private readonly ISessionManager _sessionManager;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sessionManager"></param>
        public DaasController(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> SubmitNewSession([FromBody] Session session)
        {
            var activeSession = await _sessionManager.GetActiveSession();
            if (activeSession != null)
            {
                return Conflict("There is already an existing active session");
            }

            string sessionId = await _sessionManager.SubmitNewSession(session);
            return Ok(sessionId);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> GetSessions()
        {
            return Ok(await _sessionManager.GetAllSessions());
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> GetSession(string sessionId)
        {
            return Ok(await _sessionManager.GetSession(sessionId));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> GetActiveSession()
        {
            return Ok(await _sessionManager.GetActiveSession());
        }
    }
}
