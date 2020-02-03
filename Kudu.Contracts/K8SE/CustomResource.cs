using k8s;
using k8s.Models;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Kudu.Contracts.K8SE
{
    public abstract class CustomResource : KubernetesObject
    {
        [JsonProperty(PropertyName = "metadata")]
        public V1ObjectMeta Metadata { get; set; }
    }

    public abstract class CustomResource<TSpec, TStatus> : CustomResource
    {
        [JsonProperty(PropertyName = "spec")]
        public TSpec Spec { get; set; }
        public TStatus Status { get; set; }
    }

    public abstract class CustomResourceList<T> : KubernetesObject where T : CustomResource
    {
        public V1ObjectMeta Metadata { get; set; }
        public V1ListMeta ListMetadata { get; set; }
        public List<CustomResource> Items { get; set; }
    }

    public class CustomResourceDefinition
    {
        public string ApiVersion { get; set; }

        public string PluralName { get; set; }

        public string Kind { get; set; }

        public string Namespace { get; set; }
    }
}
