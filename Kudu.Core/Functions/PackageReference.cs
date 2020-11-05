using Kudu.Contracts.Deployment;

namespace Kudu.Core.Functions
{
    using Newtonsoft.Json;

    public class PackageReference
    {
        [JsonProperty(PropertyName = "buildMetadata")]
        public string BuildMetadata { get; set; }
    }
}
