using Newtonsoft.Json;
using System.Collections.Generic;

namespace Kudu.Core.Functions
{
    public class TriggerOptions
    {
        [JsonProperty(PropertyName = "triggers")]
        public IEnumerable<ScaleTrigger> Triggers { get; set; }

        [JsonProperty(PropertyName = "pollingInterval")]
        public int? PollingInterval { get; set; }

        [JsonProperty(PropertyName = "cooldownPeriod")]
        public int? cooldownPeriod { get; set; }
    }
}
