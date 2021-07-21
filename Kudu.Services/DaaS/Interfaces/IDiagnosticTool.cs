using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Services.DaaS
{
    internal interface IDiagnosticTool
    {
        Task<DiagnosticToolResponse> InvokeAsync(string sessionId,
            string toolParams,
            string tempPath,
            string instanceId,
            CancellationToken token);
    }
}
