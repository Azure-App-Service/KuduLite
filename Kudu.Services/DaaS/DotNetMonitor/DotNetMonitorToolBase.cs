using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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

        internal async Task<IEnumerable<DotNetMonitorProcessesResponse>> GetDotNetProcessesAsync()
        {
            var resp = await _dotnetMonitorClient.GetAsync($"{dotnetMonitorAddress}/processes");
            resp.EnsureSuccessStatusCode();
            string content = await resp.Content.ReadAsStringAsync();
            var processes = JsonConvert.DeserializeObject<IEnumerable<DotNetMonitorProcessesResponse>>(content);
            return processes.Take(MaxProcessesToDiagnose).ToList();
        }

        internal async Task<DotNetMonitorProcessResponse> GetDotNetProcessAsync(int pid)
        {
            var resp = await _dotnetMonitorClient.GetAsync($"{dotnetMonitorAddress}/processes/{pid}");
            resp.EnsureSuccessStatusCode();
            string content = await resp.Content.ReadAsStringAsync();
            var process = JsonConvert.DeserializeObject<DotNetMonitorProcessResponse>(content);
            return process;
        }

        internal virtual async Task<DiagnosticToolResponse> InvokeDotNetMonitorAsync(string path, string temporaryFilePath, string fileExtension, string instanceId)
        {
            var toolResponse = new DiagnosticToolResponse();
            if (string.IsNullOrWhiteSpace(dotnetMonitorAddress))
            {
                return toolResponse;
            }

            try
            {
                var processes = await GetDotNetProcessesAsync();
                foreach (var p in processes)
                {
                    var process = await GetDotNetProcessAsync(p.pid);
                    var resp = await _dotnetMonitorClient.GetAsync(
                        path.Replace("{processId}", p.pid.ToString()),
                        HttpCompletionOption.ResponseHeadersRead);

                    if (resp.IsSuccessStatusCode)
                    {
                        string fileName = resp.Content.Headers.ContentDisposition.FileName;
                        if (string.IsNullOrWhiteSpace(fileName))
                        {
                            fileName = DateTime.UtcNow.Ticks.ToString() + fileExtension;
                        }

                        fileName = $"{instanceId}_{process.name}_{process.pid}_{fileName}";
                        fileName = Path.Combine(temporaryFilePath, fileName);
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
                        var error = await resp.Content.ReadAsStringAsync();
                        toolResponse.Errors.Add(error);
                    }
                }
            }
            catch (Exception ex)
            {
                toolResponse.Errors.Add(ex.Message);
            }

            return toolResponse;
        }

        public abstract Task<DiagnosticToolResponse> InvokeAsync(string toolParams, string tempPath, string instanceId);
    }
}
