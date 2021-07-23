using System.Collections.Generic;

namespace Kudu.Services.DaaS
{
    internal class DiagnosticToolResponse
    {
        internal List<LogFile> Logs { get; set; } = new List<LogFile>();
        internal List<string> Errors { get; set; } = new List<string>();
    }
}