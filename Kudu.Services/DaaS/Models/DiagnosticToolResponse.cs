using System.Collections.Generic;

namespace Kudu.Services.DaaS
{
    /// <summary>
    /// 
    /// </summary>
    public class DiagnosticToolResponse
    {
        /// <summary>
        /// 
        /// </summary>
        public List<LogFile> Logs { get; set; } = new List<LogFile>();
        
        /// <summary>
        /// 
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();
    }
}