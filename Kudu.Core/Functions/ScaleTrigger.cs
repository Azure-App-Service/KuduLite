using Newtonsoft.Json;
using System.Collections.Generic;

namespace Kudu.Core.Functions
{
    public class ScaleTrigger
    {
        [JsonProperty(PropertyName = "type")]
        public string Type { get; set; }

        [JsonProperty(PropertyName = "metadata")]
        public IDictionary<string, string> Metadata { get; set; }

        [JsonProperty(PropertyName = "authenticationRef")]
        public IDictionary<string, string> AuthenticationRef { get; set; }
    }
}
