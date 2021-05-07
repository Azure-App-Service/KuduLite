using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AspNetCore.Proxy;
using Kudu.Contracts.Diagnostics;
using Kudu.Services.Arm;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Kudu.Services.Performance
{
    // This is a placeholder for future process API functionality on Linux,
    // the implementation of which will differ from Windows enought that it warrants
    // a separate controller class. For now this returns 400s for all routes.

    public class LinuxProcessController : Controller
    {
        const string dotnetMonitorPort = "50051";
        const string DotNetMonitorAddressCacheKey = "DotNetMonitorAddressCacheKey";

        private IMemoryCache _cache;

        public LinuxProcessController(IMemoryCache memoryCache)
        {
            _cache = memoryCache;
        }

        private const string ERRORMSG = "Not supported on Linux";
        private const string DOTNETMONITORSTOPPED = "The dotnet-monitor tool is not running";

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
        public Task GetAllProcesses(bool allUsers = false)
        {
            if (IsDotNetMonitorEnabled())
            {
                var dotnetMonitorAddress = GetDotNetMonitorAddress();
                if (!string.IsNullOrWhiteSpace(dotnetMonitorAddress))
                {
                    return this.HttpProxyAsync($"{dotnetMonitorAddress}/processes");
                }
                else
                {
                    Response.StatusCode = (int)HttpStatusCode.NotFound;
                    return Task.FromResult(Response.Body.WriteAsync(Encoding.UTF8.GetBytes(DOTNETMONITORSTOPPED)));
                }
            }

            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return Task.FromResult(Response.Body.WriteAsync(Encoding.UTF8.GetBytes(ERRORMSG)));
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpGet]
        public Task GetProcess(int id)
        {
            if (IsDotNetMonitorEnabled())
            {
                var dotnetMonitorAddress = GetDotNetMonitorAddress();
                if (!string.IsNullOrWhiteSpace(dotnetMonitorAddress))
                {
                    return this.HttpProxyAsync($"{dotnetMonitorAddress}/processes/{id}");
                }
                else
                {
                    Response.StatusCode = (int)HttpStatusCode.NotFound;
                    return Task.FromResult(Response.Body.WriteAsync(Encoding.UTF8.GetBytes(DOTNETMONITORSTOPPED)));
                }
            }

            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return Task.FromResult(Response.Body.WriteAsync(Encoding.UTF8.GetBytes(ERRORMSG)));
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
        public Task MiniDump(int id, string type = "WithHeap")
        {
            if (IsDotNetMonitorEnabled())
            {
                var dotnetMonitorAddress = GetDotNetMonitorAddress();
                if (!string.IsNullOrWhiteSpace(dotnetMonitorAddress))
                {
                    return this.HttpProxyAsync($"{dotnetMonitorAddress}/dump/{id}?type={type}");
                }

                Response.StatusCode = (int)HttpStatusCode.NotFound;
                return Task.FromResult(Response.Body.WriteAsync(Encoding.UTF8.GetBytes(DOTNETMONITORSTOPPED)));

            }

            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return Task.FromResult(Response.Body.WriteAsync(Encoding.UTF8.GetBytes(ERRORMSG)));
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

        private string GetDotNetMonitorAddress()
        {
            var dotnetMonitorAddress = _cache.GetOrCreate(DotNetMonitorAddressCacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                var ipAddress = GetIpAddress();
                if (!string.IsNullOrWhiteSpace(ipAddress))
                {
                    return $"http://{ipAddress}:{dotnetMonitorPort}";
                }
                return string.Empty;
            });

            return dotnetMonitorAddress;
        }

        private string GetIpAddress()
        {
            try
            {
                string ipAddress = System.IO.File.ReadAllText("/appsvctmp/ipaddr_" + Environment.GetEnvironmentVariable("WEBSITE_ROLE_INSTANCE_ID"));
                if (ipAddress != null)
                {
                    if (ipAddress.Contains(':'))
                    {
                        string[] ipAddrPortStr = ipAddress.Split(":");
                        return ipAddrPortStr[0];
                    }
                    else
                    {
                        return ipAddress;
                    }
                }
            }
            catch (Exception)
            {
            }
            return string.Empty;
        }

        private bool IsDotNetMonitorEnabled()
        {
            string val = Environment.GetEnvironmentVariable("WEBSITE_USE_DOTNET_MONITOR");
            if (!string.IsNullOrWhiteSpace(val))
            {
                string stack = Environment.GetEnvironmentVariable("WEBSITE_STACK");

                if (!string.IsNullOrWhiteSpace(stack))
                {
                    return val.Equals("true", StringComparison.OrdinalIgnoreCase)
                        && stack.Equals("DOTNETCORE", StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }
    }
}