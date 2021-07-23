using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Kudu.Services.DaaS
{
    public class Session
    {
        public string SessionId { get; set; }
        public Status Status { get; set; }
        public DateTime StartTime { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public DiagnosticTool Tool { get; set; }
        public string ToolParams { get; set; }
        public List<string> Instances { get; set; }
        public List<ActiveInstance> ActiveInstances { get; set; }
        public DateTime EndTime { get; set; }
        
    }
}
