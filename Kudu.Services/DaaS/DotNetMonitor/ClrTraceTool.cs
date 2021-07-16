using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Kudu.Services.Performance
{
    class ClrTraceTool : DotNetMonitorToolBase
    {
        public override async Task<IEnumerable<LogFile>> InvokeAsync(string toolParams, string tempPath, string instanceId)
        {
            ClrTraceParams clrTraceParams = new ClrTraceParams(toolParams);
            var logs = new List<LogFile>();

            if (!string.IsNullOrWhiteSpace(dotnetMonitorAddress))
            {
                var processes = await GetDotNetProcesses();
                foreach (var p in processes)
                {
                    var process = await GetDotNetProcess(p.pid);
                    var resp = await _dotnetMonitorClient.GetAsync(
                        $"{dotnetMonitorAddress}/trace/{p.pid}?durationSeconds={clrTraceParams.DurationSeconds}&profile={clrTraceParams.TraceProfile}",
                        HttpCompletionOption.ResponseHeadersRead);

                    if (resp.IsSuccessStatusCode)
                    {
                        string fileName = resp.Content.Headers.ContentDisposition.FileName;
                        if (string.IsNullOrWhiteSpace(fileName))
                        {
                            fileName = DateTime.UtcNow.Ticks.ToString() + ".etl";
                        }

                        fileName = $"{instanceId}_{process.name}_{process.pid}_{fileName}";
                        fileName = Path.Combine(tempPath, fileName);
                        using (var stream = await resp.Content.ReadAsStreamAsync())
                        {
                            using (var fileStream = new FileStream(fileName, FileMode.CreateNew))
                            {
                                await stream.CopyToAsync(fileStream);
                            }
                        }

                        logs.Add(new LogFile()
                        {
                            FullPath = fileName,
                            ProcessName = process.name,
                            ProcessId = process.pid
                        });
                    }
                    else
                    {
                        var error = await resp.Content.ReadAsStringAsync();
                        LogError("MemoryDumpTool-InvokeAsync", "Failed while calling dotnet monitor", error);
                    }
                }
            }

            return logs;
        }
    }
}
