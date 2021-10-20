using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Functions
{
    public class TriggerAuthSecretTarget
    {
        [JsonProperty(PropertyName = "parameter")]
        public string parameter { get; set; }

        [JsonProperty(PropertyName = "name")]
        public string name { get; set; }

        [JsonProperty(PropertyName = "key")]
        public string key { get; set; }
    }
}
