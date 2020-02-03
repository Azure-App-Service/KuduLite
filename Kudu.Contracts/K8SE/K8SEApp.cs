
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Kudu.Contracts.K8SE
{
        public enum AppKind
        {
            CodeWebApp = 0,
            FunctionApp = 1,
            ContainerApp = 2
        }
        public class AppSpec
        {
            [JsonProperty(PropertyName = "kind")]
            public string Kind { get; set; }

            [JsonProperty(PropertyName = "appPort")]
            public int AppPort { get; set; }

            [JsonProperty(PropertyName = "minReplicas")]
            public int? MinReplicas { get; set; }

            [JsonProperty(PropertyName = "maxReplicas")]
            public int? MaxReplicas { get; set; }

            [JsonProperty(PropertyName = "hostnames")]
            public CodeAppSpec codeAppSpec { get; set; }

            [JsonProperty(PropertyName = "containers")]
            public List<ContainerSpec> Containers { get; set; }

            [JsonProperty(PropertyName = "hostnames")]
            public List<HostNameSpec> Hostnames { get; set; }
        }

        public class ContainerSpec
        {
            [JsonProperty(PropertyName = "type")]
            public string Type { get; set; }

            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }

            [JsonProperty(PropertyName = "image")]
            public string Image { get; set; }

            [JsonProperty(PropertyName = "ports")]
            public List<PortSpec> Ports { get; set; }
        }

        public class HostNameSpec
        {
            [JsonProperty(PropertyName = "domain")]
            public string Domain { get; set; }
        }

        public class CodeAppSpec
        {
            [JsonProperty(PropertyName = "framework")]
            public string Framework { get; set; }

            [JsonProperty(PropertyName = "frameworkVersion")]
            public string FrameworkVersion { get; set; }

            [JsonProperty(PropertyName = "functionRuntimeVersion")]
            public string FunctionRuntimeVersion { get; set; }

            [JsonProperty(PropertyName = "buildVersion")]
            public string BuildVersion { get; set; }
        }

        public class PortSpec
        {
            [JsonProperty(PropertyName = "containerPort")]
            public int ContainerPort { get; set; }

            [JsonProperty(PropertyName = "name")]
            public string Name { get; set; }
        }

        public class AppStatus
        {
        }

        public class K8SEApp : CustomResource<AppSpec, AppStatus>
        {
        }
}
