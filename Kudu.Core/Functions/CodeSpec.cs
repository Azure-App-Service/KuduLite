namespace Kudu.Core.Functions
{
    using Newtonsoft.Json;

    public class CodeSpec
    {
        [JsonProperty(PropertyName = "packageRef")]
        public PackageReference PackageRef { get; set; }
    }
}
