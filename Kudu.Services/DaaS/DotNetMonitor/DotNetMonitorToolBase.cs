using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Kudu.Services.Performance
{
    abstract class DotNetMonitorToolBase : IDiagnosticTool
    {
        protected const string EgressProviderName = "monitorFile";
        protected readonly HttpClient _dotnetMonitorClient;
        protected readonly string dotnetMonitorAddress = DotNetHelper.GetDotNetMonitorAddress();

        public DotNetMonitorToolBase()
        {
            _dotnetMonitorClient = CreateDotNetMonitorClient();
        }

        private HttpClient CreateDotNetMonitorClient()
        {
            var webRequestHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
                {
                    return true;
                }
            };

            return new HttpClient(webRequestHandler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
        }

        internal async Task<IEnumerable<DotNetMonitorProcessesResponse>> GetDotNetProcesses()
        {
            var resp = await _dotnetMonitorClient.GetAsync($"{dotnetMonitorAddress}/processes");
            resp.EnsureSuccessStatusCode();
            string content = await resp.Content.ReadAsStringAsync();
            var processes = JsonConvert.DeserializeObject<IEnumerable<DotNetMonitorProcessesResponse>>(content);
            return processes;
        }

        internal async Task<DotNetMonitorProcessResponse> GetDotNetProcess(int pid)
        {
            var resp = await _dotnetMonitorClient.GetAsync($"{dotnetMonitorAddress}/processes/{pid}");
            resp.EnsureSuccessStatusCode();
            string content = await resp.Content.ReadAsStringAsync();
            var process = JsonConvert.DeserializeObject<DotNetMonitorProcessResponse>(content);
            return process;
        }

        public abstract Task<IEnumerable<LogFile>> InvokeAsync(string toolParams, string tempPath);
    }
}
