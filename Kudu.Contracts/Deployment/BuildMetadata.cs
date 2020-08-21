using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Contracts.Deployment
{
    public class BuildMetadata
    {
        [JsonProperty(PropertyName = "appName")]
        public string AppName;

        [JsonProperty(PropertyName = "buildVersion")]
        public string BuildVersion;

        [JsonProperty(PropertyName = "appSubPath")]
        public string AppSubPath;
    }
}
