using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AspNetCore.Proxy;
using AspNetCore.Proxy.Options;
using Kudu.Contracts.Diagnostics;
using Kudu.Core.Helpers;
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
        const string AcceptEncodingHeader = "Accept-Encoding";

        private readonly IMemoryCache _cache;
        private readonly HttpProxyOptions _options;

        public LinuxProcessController(IMemoryCache memoryCache)
        {
            _cache = memoryCache;
            _options = HttpProxyOptionsBuilder.Instance.WithHttpClientName("DotnetMonitorProxyClient")
                .WithBeforeSend((context, request) =>
                {
                    // Remove Accept encoding header from requests due to a
                    // known issue in dotnet-monitor with brotli compression
                    // https://github.com/dotnet/dotnet-monitor/issues/330

                    if (request.Headers.Contains(AcceptEncodingHeader))
                    {
                        request.Headers.Remove(AcceptEncodingHeader);
                    }

                    return Task.CompletedTask;
                })
                .Build();
        }

        private const string ERRORMSG = "Not supported on Linux";
        private const string DOTNETMONITORNOTCONFIGURED = "The dotnet-monitor tool is not configured to run";

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
            return ExecuteIfDotnetMonitorEnabled((dotnetMonitorAddress) =>
            {
                return this.HttpProxyAsync($"{dotnetMonitorAddress}/processes", _options);
            });
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpGet]
        public Task GetProcess(int id)
        {
            return ExecuteIfDotnetMonitorEnabled((dotnetMonitorAddress) =>
            {
                return this.HttpProxyAsync($"{dotnetMonitorAddress}/processes/{id}", _options);
            });
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
            return ExecuteIfDotnetMonitorEnabled((dotnetMonitorAddress) =>
            {
                return this.HttpProxyAsync($"{dotnetMonitorAddress}/dump/{id}?type={type}", _options);
            });
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "Parameters preserved for equivalent route binding")]
        [HttpGet]
        public Task StartProfileAsync(int id, int durationSeconds = 60, string profile = "Cpu,Http,Metrics")
        {
            return ExecuteIfDotnetMonitorEnabled((dotnetMonitorAddress) =>
            {
                return this.HttpProxyAsync($"{dotnetMonitorAddress}/trace/{id}?profile={profile}&durationSeconds={durationSeconds}", _options);
            });
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

        private Task ExecuteIfDotnetMonitorEnabled(Func<string, Task> action)
        {
            if (DotNetHelper.IsDotNetMonitorEnabled())
            {
                var dotnetMonitorAddress = GetDotNetMonitorAddress();
                if (!string.IsNullOrWhiteSpace(dotnetMonitorAddress))
                {
                    return action(dotnetMonitorAddress);
                }

                Response.StatusCode = (int)HttpStatusCode.NotFound;
                return Task.FromResult(Response.Body.WriteAsync(Encoding.UTF8.GetBytes(DOTNETMONITORNOTCONFIGURED)));
            }

            Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return Task.FromResult(Response.Body.WriteAsync(Encoding.UTF8.GetBytes(ERRORMSG)));
        }

        private string GetDotNetMonitorAddress()
        {
            if (OSDetector.IsOnWindows())
            {
                return "http://localhost:52323";
            }

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
    }
}