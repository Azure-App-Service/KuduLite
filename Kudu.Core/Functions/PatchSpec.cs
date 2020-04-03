using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Functions
{
    public class PatchSpec
    {
        [JsonProperty(PropertyName = "triggerOptions")]
        public TriggerOptions TriggerOptions { get; set; }

        [JsonProperty(PropertyName = "code")]
        public CodeSpec Code { get; set; }
    }
}
