using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kudu.Services.Performance
{
    internal interface IDiagnosticTool
    {
        Task<IEnumerable<string>> InvokeAsync(string diagnoserParams);
    }
}
