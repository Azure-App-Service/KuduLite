using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Kudu.Services.DaaS
{
    /// <summary>
    /// 
    /// </summary>
    
    [JsonConverter(typeof(StringEnumConverter))]
    public enum Status
    {
        Active,
        Started,
        Complete,
        TimedOut
    }
}
