using System;
using System.Collections.Generic;

namespace Kudu.Services.Performance
{
    /// <summary>
    /// 
    /// </summary>
    public class Session
    {
        /// <summary>
        /// 
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public Status Status { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public DiagnosticTool Tool { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string ToolParams { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public string BlobSasUri { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public List<string> Instances { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public List<ActiveInstance> ActiveInstances { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public DateTime EndTime { get; set; }
        
    }
}
