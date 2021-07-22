using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Services.Performance;
using Newtonsoft.Json;

namespace Kudu.Services.DaaS
{
    abstract internal class DotNetMonitorToolBase : IDiagnosticTool
    {
        const int MaxProcessesToDiagnose = 5;
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

        internal async Task<IEnumerable<DotNetMonitorProcessesResponse>> GetDotNetProcessesAsync(CancellationToken cancellationToken)
        {
            var resp = await _dotnetMonitorClient.GetAsync($"{dotnetMonitorAddress}/processes", cancellationToken);
            resp.EnsureSuccessStatusCode();
            string content = await resp.Content.ReadAsStringAsync();
            var processes = JsonConvert.DeserializeObject<IEnumerable<DotNetMonitorProcessesResponse>>(content);
            return processes.Take(MaxProcessesToDiagnose).ToList();
        }

        internal async Task<DotNetMonitorProcessResponse> GetDotNetProcessAsync(int pid, CancellationToken cancellationToken)
        {
            var resp = await _dotnetMonitorClient.GetAsync($"{dotnetMonitorAddress}/processes/{pid}", cancellationToken);
            resp.EnsureSuccessStatusCode();
            string content = await resp.Content.ReadAsStringAsync();
            var process = JsonConvert.DeserializeObject<DotNetMonitorProcessResponse>(content);
            return process;
        }

        internal async Task UpdateToolResponseAsync(string sessionId,
            DiagnosticToolResponse toolResponse,
            DotNetMonitorProcessResponse process,
            HttpResponseMessage resp,
            string fileExtension,
            string temporaryFilePath,
            string instanceId)
        {
            DaasLogger.LogSessionMessage($"dotnet-monitor response status {resp.StatusCode}", sessionId);

            if (resp.IsSuccessStatusCode)
            {
                string fileName = resp.Content.Headers.ContentDisposition.FileName;
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = DateTime.UtcNow.Ticks.ToString() + fileExtension;
                }

                fileName = Path.Combine(temporaryFilePath, $"{instanceId}_{process.name}_{process.pid}_{fileName}");
                using (var stream = await resp.Content.ReadAsStreamAsync())
                {
                    using (var fileStream = new FileStream(fileName, FileMode.CreateNew))
                    {
                        await stream.CopyToAsync(fileStream);
                    }
                }

                toolResponse.Logs.Add(new LogFile()
                {
                    FullPath = fileName,
                    ProcessName = process.name,
                    ProcessId = process.pid
                });
            }
            else
            {
                var error = await GetErrorResponse(resp);
                toolResponse.Errors.Add(error);
                DaasLogger.LogSessionError($"dotnet-monitor returned an error response", sessionId, error);
            }
        }

        private async Task<string> GetErrorResponse(HttpResponseMessage resp)
        {
            var error = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(error))
            {
                return $"dotnet-monitor failed with Status:{resp.StatusCode}";
            }
            
            return error;
        }

        internal virtual async Task<DiagnosticToolResponse> InvokeDotNetMonitorAsync(
            string sessionId,
            string path,
            string temporaryFilePath,
            string fileExtension,
            string instanceId,
            CancellationToken cancellationToken)
        {
            DaasLogger.LogSessionMessage("InvokeDotNetMonitorAsync called", sessionId);
            var toolResponse = new DiagnosticToolResponse();
            if (string.IsNullOrWhiteSpace(dotnetMonitorAddress))
            {
                return toolResponse;
            }

            try
            {
                var processes = await GetDotNetProcessesAsync(cancellationToken);
                foreach (var p in processes)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var process = await GetDotNetProcessAsync(p.pid, cancellationToken);
                    path = path.Replace("{processId}", p.pid.ToString());
                    
                    DaasLogger.LogSessionMessage($"Invoking {path}", sessionId);
                    var resp = await _dotnetMonitorClient.GetAsync(path,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);

                    await UpdateToolResponseAsync(sessionId, 
                        toolResponse,
                        process,
                        resp,
                        fileExtension,
                        temporaryFilePath,
                        instanceId);
                }
            }
            catch (Exception ex)
            {
                DaasLogger.LogSessionError($"Failed while invoking dotnet-monitor", sessionId, ex);
                toolResponse.Errors.Add(ex.Message);
            }

            return toolResponse;
        }

        public abstract Task<DiagnosticToolResponse> InvokeAsync(string sessionId, string toolParams, string tempPath, string instanceId, CancellationToken token);
    }
}
