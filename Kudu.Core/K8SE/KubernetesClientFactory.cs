using System;
using System.Net.Http;
using k8s;
using Microsoft.Rest.TransientFaultHandling;

namespace Kudu.Core.K8SE
{
    public class KubernetesClientFactory : IKubernetesClientFactory
    {
        public const int ClientRetryCount = 3;
        public const int ClientRetryIntervalInSeconds = 5;

        private readonly IHttpClientFactory clientFactory;

        public KubernetesClientFactory(IHttpClientFactory clientFactory)
        {
            this.clientFactory = clientFactory;
        }

        public IKubernetes CreateClient()
        {
            var config = KubernetesClientConfiguration.BuildDefaultConfig();

            var client = new Kubernetes(config, this.clientFactory.CreateClient());

            var retryPolicy = new RetryPolicy(
                new HttpTransientErrorDetectionStrategy(),
                ClientRetryCount,
                TimeSpan.FromSeconds(ClientRetryIntervalInSeconds));

            client.SetRetryPolicy(retryPolicy);

            return client;
        }
    }
}
