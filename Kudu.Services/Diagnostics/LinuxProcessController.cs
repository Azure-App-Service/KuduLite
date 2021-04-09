using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using Kudu.Contracts.Diagnostics;
using Kudu.Services.Arm;
using Microsoft.AspNetCore.Mvc;

namespace Kudu.Services.Performance
{
    // This is a placeholder for future process API functionality on Linux,
    // the implementation of which will differ from Windows enought that it warrants
    // a separate controller class. For now this returns 400s for all routes.

    public class LinuxProcessController : Controller
    {
        private const string ERRORMSG = "Not supported on Linux";

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpGet]
        public IActionResult GetThread(int processId, int threadId)
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return new JsonResult(ERRORMSG);
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpGet]
        public IActionResult GetAllThreads(int id)
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return new JsonResult(ERRORMSG);
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpGet]
        public IActionResult GetModule(int id, string baseAddress)
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return new JsonResult(ERRORMSG);
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpGet]
        public IActionResult GetAllModules(int id)
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return new JsonResult(ERRORMSG);
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpGet]
        public IActionResult GetAllProcesses(bool allUsers = false)
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return new JsonResult(ERRORMSG);
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpGet]
        public IActionResult GetProcess(int id)
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return new JsonResult(ERRORMSG);
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpDelete]
        public IActionResult KillProcess(int id)
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return new JsonResult(ERRORMSG);
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpGet]
        public IActionResult MiniDump(int id, int dumpType = 0, string format = null)
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return new JsonResult(ERRORMSG);
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpPost]
        public IActionResult StartProfileAsync(int id, bool iisProfiling = false)
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return new JsonResult(ERRORMSG);
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpGet]
        public IActionResult StopProfileAsync(int id)
        {
            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return new JsonResult(ERRORMSG);
        }

        [HttpGet]
        public IActionResult GetEnvironments(int id, string filter)
        {
            var envs = System.Environment.GetEnvironmentVariables()
                .Cast<DictionaryEntry>().ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value.ToString())
                .Where(p => string.Equals("all", filter, StringComparison.OrdinalIgnoreCase) || p.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToDictionary(p => p.Key, p => p.Value);

            return Ok(ArmUtils.AddEnvelopeOnArmRequest(new ProcessEnvironmentInfo(filter, envs), Request));
        }
    }
}