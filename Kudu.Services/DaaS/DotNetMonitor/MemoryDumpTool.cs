using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Kudu.Services.Performance
{
    internal class MemoryDumpTool : DotNetMonitorToolBase
    {
        public override async Task<IEnumerable<LogFile>> InvokeAsync(string toolParams, string tempPath)
        {
            MemoryDumpParams memoryDumpParams = new MemoryDumpParams(toolParams);
            var logs = new List<LogFile>();

            if (!string.IsNullOrWhiteSpace(dotnetMonitorAddress))
            {
                var processes = await GetDotNetProcesses();
                foreach (var p in processes)
                {
                    var process = await GetDotNetProcess(p.pid);
                    var requestPath = $"{dotnetMonitorAddress}/dump/{p.pid}?type={memoryDumpParams.DumpType}";
                    var resp = await _dotnetMonitorClient.GetAsync(requestPath, HttpCompletionOption.ResponseHeadersRead);
                    if (resp.IsSuccessStatusCode)
                    {
                        string fileName = resp.Content.Headers.ContentDisposition.FileName;
                        if (string.IsNullOrWhiteSpace(fileName))
                        {
                            fileName = DateTime.UtcNow.Ticks.ToString() + ".dmp";
                        }

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
                    }
                }
            }

            return logs;
        }
    }
}
