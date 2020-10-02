using Kudu.Contracts.Deployment;

namespace Kudu.Core.Functions
{
    using Newtonsoft.Json;

    public class PackageReference
    {
        [JsonProperty(PropertyName = "buildVersion")]
        public BuildMetadata BuildVersion { get; set; }
    }
}
