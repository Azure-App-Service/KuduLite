using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Functions
{
    public class PatchTriggerAuthJson
    {
        [JsonProperty(PropertyName = "spec")]
        public TriggerAuthSpec TriggerAuthSpec { get; set; }
    }
}
