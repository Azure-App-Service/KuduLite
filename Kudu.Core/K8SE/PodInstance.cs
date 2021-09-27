using Newtonsoft.Json;

namespace Kudu.Core.K8SE
{
    public class PodInstance
    {
        [JsonProperty(PropertyName = "name")]
        public string Name { get; set; }

        [JsonProperty(PropertyName = "ipAddr")]
        public string IpAddress { get; set; }

        [JsonProperty(PropertyName = "hostIp")]
        public string HostIpAddress { get; set; }

        [JsonProperty(PropertyName = "nodeName")]
        public string NodeName { get; set; }

        [JsonProperty(PropertyName = "startTime")]
        public string StartTime { get; set; }
    }
}
