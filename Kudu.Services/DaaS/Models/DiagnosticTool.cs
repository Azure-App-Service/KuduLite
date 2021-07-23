using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Kudu.Services.DaaS
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum DiagnosticTool
    {
        Unspecified,
        MemoryDump,
        Profiler
    }
}
