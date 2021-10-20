using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Functions
{
    public class TriggerAuthSpec
    {
        [JsonProperty(PropertyName = "secretTargetRef", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<TriggerAuthSecretTarget> SecretTargetRef { get; set; }
    }
}
