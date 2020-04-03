namespace Kudu.Core.Functions
{
    using Newtonsoft.Json;

    public class PackageReference
    {
        [JsonProperty(PropertyName = "buildVersion")]
        public string BuildVersion { get; set; }
    }
}
