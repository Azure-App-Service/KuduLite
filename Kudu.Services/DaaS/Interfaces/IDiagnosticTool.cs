using System.Threading.Tasks;

namespace Kudu.Services.DaaS
{
    internal interface IDiagnosticTool
    {
        Task<DiagnosticToolResponse> InvokeAsync(string toolParams, string tempPath, string instanceId);
    }
}
