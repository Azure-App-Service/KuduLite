using System.Threading.Tasks;

namespace Kudu.Services.DaaS
{
    internal class MemoryDumpTool : DotNetMonitorToolBase
    {
        public override async Task<DiagnosticToolResponse> InvokeAsync(string toolParams, string temporaryFilePath, string instanceId)
        {
            MemoryDumpParams memoryDumpParams = new MemoryDumpParams(toolParams);
            string path = $"{dotnetMonitorAddress}/dump/{{processId}}?type={memoryDumpParams.DumpType}";
            var response = await InvokeDotNetMonitorAsync(path, temporaryFilePath, ".dmp", instanceId);
            return response;
        }
    }
}
