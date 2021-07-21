using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Services.DaaS
{
    class ClrTraceTool : DotNetMonitorToolBase
    {
        internal override async Task<DiagnosticToolResponse> InvokeDotNetMonitorAsync(string path,
            string temporaryFilePath,
            string fileExtension,
            string instanceId,
            CancellationToken cancellationToken)
        {
            var toolResponse = new DiagnosticToolResponse();
            if (string.IsNullOrWhiteSpace(dotnetMonitorAddress))
            {
                return toolResponse;
            }

            try
            {
                var tasks = new Dictionary<DotNetMonitorProcessResponse, Task<HttpResponseMessage>>();
                foreach (var p in await GetDotNetProcessesAsync(cancellationToken))
                {
                    var process = await GetDotNetProcessAsync(p.pid, cancellationToken);
                    tasks.Add(process, _dotnetMonitorClient.GetAsync(
                        path.Replace("{processId}", p.pid.ToString()),
                        HttpCompletionOption.ResponseHeadersRead));
                }

                foreach (var task in tasks)
                {
                    var process = task.Key;
                    var resp = await task.Value;

                    await UpdateToolResponseAsync(toolResponse,
                        process,
                        resp,
                        fileExtension,
                        temporaryFilePath,
                        instanceId);
                }
            }
            catch (Exception ex)
            {
                toolResponse.Errors.Add(ex.Message);
            }

            return toolResponse;
        }

        public override async Task<DiagnosticToolResponse> InvokeAsync(string toolParams, string temporaryFilePath, string instanceId, CancellationToken token)
        {
            ClrTraceParams clrTraceParams = new ClrTraceParams(toolParams);
            string path = $"{dotnetMonitorAddress}/trace/{{processId}}?durationSeconds={clrTraceParams.DurationSeconds}&profile={clrTraceParams.TraceProfile}";
            var response = await InvokeDotNetMonitorAsync(path, temporaryFilePath, fileExtension: ".nettrace", instanceId, token);
            return response;
        }
    }
}
