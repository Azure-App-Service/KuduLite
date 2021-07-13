using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Kudu.Services.Performance
{
    internal class MemoryDumpTool : DotNetMonitorToolBase
    {
        public override async Task<IEnumerable<LogFile>> InvokeAsync(string toolParams)
        {
            MemoryDumpParams memoryDumpParams = new MemoryDumpParams(toolParams);
            var logs = new List<LogFile>();

            if (!string.IsNullOrWhiteSpace(dotnetMonitorAddress))
            {
                var processes = await GetDotNetProcesses();
                foreach (var p in processes)
                {
                    var process = await GetDotNetProcess(p.pid);
                    var resp = await _dotnetMonitorClient.GetAsync($"{dotnetMonitorAddress}/dump/{p.pid}?egressProvider={EgressProviderName}&type={memoryDumpParams.DumpType}");
                    if (resp.IsSuccessStatusCode)
                    {
                        string responseBody = await resp.Content.ReadAsStringAsync();
                        var dotnetMonitorResponse = JsonConvert.DeserializeObject<DotNetMonitorMemoryDumpResponse>(responseBody);
                        logs.Add(new LogFile()
                        {
                            FullPath = dotnetMonitorResponse.Path,
                            ProcessName = process.name,
                            ProcessId = process.pid
                        });
                    }
                }
            }

            return logs;
        }
    }
}
