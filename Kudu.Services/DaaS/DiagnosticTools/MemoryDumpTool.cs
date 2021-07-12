using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Kudu.Services.Performance
{
    internal class MemoryDumpTool : IDiagnosticTool
    {
        private const string EgressProviderName = "monitorFile";
        private readonly HttpClient _dotnetMonitorClient;

        public MemoryDumpTool()
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

        public async Task<IEnumerable<string>> InvokeAsync(string diagnoserParams)
        {
            MemoryDumpParams memoryDumpParams = new MemoryDumpParams(diagnoserParams);
            var logs = new List<string>();
            
            var dotnetMonitorAddress = DotNetHelper.GetDotNetMonitorAddress();
            if (!string.IsNullOrWhiteSpace(dotnetMonitorAddress))
            {
                var resp = await _dotnetMonitorClient.GetAsync($"{dotnetMonitorAddress}/dump/7152?egressProvider={EgressProviderName}&type={memoryDumpParams.DumpType}");
                if (resp.IsSuccessStatusCode)
                {
                    string responseBody = await resp.Content.ReadAsStringAsync();
                    var dotnetMonitorResponse = JsonConvert.DeserializeObject<DotNetMonitorMemoryDumpResponse>(responseBody);
                    logs.Add(dotnetMonitorResponse.Path);
                }
            }

            return logs;
        }

    }
}
