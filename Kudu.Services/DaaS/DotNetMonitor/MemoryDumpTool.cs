using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Services.DaaS
{
    internal class MemoryDumpTool : DotNetMonitorToolBase
    {
        public override async Task<DiagnosticToolResponse> InvokeAsync(string sessionId,
            string toolParams,
            string temporaryFilePath,
            string instanceId,
            CancellationToken cancellationToken)
        {
            MemoryDumpParams memoryDumpParams = new MemoryDumpParams(toolParams);
            string path = $"{dotnetMonitorAddress}/dump/{{processId}}?type={memoryDumpParams.DumpType}";
            var response = await InvokeDotNetMonitorAsync(sessionId, path, temporaryFilePath, ".dmp", instanceId, cancellationToken);
            return response;
        }
    }
}
