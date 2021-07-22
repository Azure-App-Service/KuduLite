using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kudu.Services.DaaS
{
    /// <summary>
    /// Controller exposing the DaaS functionality
    /// </summary>
    public class DaasController : Controller
    {
        private readonly ISessionManager _sessionManager;

        public DaasController(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
        }

        [HttpPost]
        public async Task<IActionResult> SubmitNewSession([FromBody] Session session)
        {
            if (session.Tool == DiagnosticTool.Unspecified)
            {
                return BadRequest("Please specify a valid diagnostic tool");
            }

            try
            {
                string sessionId = await _sessionManager.SubmitNewSessionAsync(session);
                return Ok(sessionId);
            }
            catch (AccessViolationException aex)
            {
                return Conflict(aex.Message);
            }
            catch(Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetSessions()
        {
            return Ok(await _sessionManager.GetAllSessionsAsync());
        }

        [HttpGet]
        public async Task<IActionResult> GetSession(string sessionId)
        {
            return Ok(await _sessionManager.GetSessionAsync(sessionId));
        }

        [HttpGet]
        public async Task<IActionResult> GetActiveSession()
        {
            return Ok(await _sessionManager.GetActiveSessionAsync());
        }
    }
}
