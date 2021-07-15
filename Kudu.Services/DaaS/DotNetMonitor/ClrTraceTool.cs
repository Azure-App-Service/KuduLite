using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Kudu.Services.Performance
{
    class ClrTraceTool : DotNetMonitorToolBase
    {
        public override async Task<IEnumerable<LogFile>> InvokeAsync(string toolParams, string tempPath)
        {
            ClrTraceParams clrTraceParams = new ClrTraceParams(toolParams);
            var logs = new List<LogFile>();

            if (!string.IsNullOrWhiteSpace(dotnetMonitorAddress))
            {
                var processes = await GetDotNetProcesses();
                foreach (var p in processes)
                {
                    var process = await GetDotNetProcess(p.pid);
                    var resp = await _dotnetMonitorClient.GetAsync($"{dotnetMonitorAddress}/trace/{p.pid}?egressProvider={EgressProviderName}&durationSeconds={clrTraceParams.DurationSeconds}&profile={clrTraceParams.TraceProfile}");
                    resp.EnsureSuccessStatusCode();

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

            return logs;
        }
    }
}
