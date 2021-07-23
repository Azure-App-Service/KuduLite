using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Services.DaaS
{
    class ClrTraceTool : DotNetMonitorToolBase
    {
        internal override async Task<DiagnosticToolResponse> InvokeDotNetMonitorAsync(string sessionId,
            string path,
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
                    path = path.Replace("{processId}", p.pid.ToString());
                    
                    DaasLogger.LogSessionMessage($"Invoking {path}", sessionId);
                    tasks.Add(process, _dotnetMonitorClient.GetAsync(path,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken));
                }

                //
                // Since the trace will run for the duration specified, invoke the 
                // trace API for all the processes in parallel
                // 
                foreach (var task in tasks)
                {
                    var process = task.Key;
                    var resp = await task.Value;

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

        public override async Task<DiagnosticToolResponse> InvokeAsync(string sessionId,
            string toolParams,
            string temporaryFilePath,
            string instanceId,
            CancellationToken token)
        {
            ClrTraceParams clrTraceParams = new ClrTraceParams(toolParams);
            string path = $"{dotnetMonitorAddress}/trace/{{processId}}?durationSeconds={clrTraceParams.DurationSeconds}&profile={clrTraceParams.TraceProfile}";
            var response = await InvokeDotNetMonitorAsync(sessionId, path, temporaryFilePath, fileExtension: ".nettrace", instanceId, token);
            return response;
        }
    }
}
