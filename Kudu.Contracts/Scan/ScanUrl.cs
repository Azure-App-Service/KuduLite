using Kudu.Contracts.Infrastructure;
using Newtonsoft.Json;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Kudu.Contracts.Scan
{
    public class ScanUrl : INamedObject
    {
        [JsonProperty(PropertyName = "track_url")]
        public String TrackingURL { get; set; }

        [JsonProperty(PropertyName = "result_url")]
        public String ResultURL { get; set; }

        [JsonIgnore]
        [SuppressMessage("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes", Justification = "to provide ARM spceific name")]
        string INamedObject.Name { get { return Id; } }

        [JsonProperty(PropertyName = "id")]
        public String Id { get; set; }

        public ScanUrl(string trackingURL, string resultURL, string id)
        {
            TrackingURL = trackingURL;
            ResultURL = resultURL;
            Id = id;
        }
    }
}
