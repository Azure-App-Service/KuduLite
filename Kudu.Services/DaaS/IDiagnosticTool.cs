using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kudu.Services.Performance
{
    internal interface IDiagnosticTool
    {
        Task<IEnumerable<LogFile>> InvokeAsync(string toolParams);
    }
}
