using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Functions
{
    public class PatchAppJson
    {
        [JsonProperty(PropertyName = "spec")]
        public PatchSpec PatchSpec { get; set; }
    }
}
