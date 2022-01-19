using System;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using k8s;
using Microsoft.Rest;
using Microsoft.Rest.TransientFaultHandling;

namespace Kudu.Core.K8SE
{
    public class KubernetesClientFactory : IKubernetesClientFactory
    {
        public const int ClientRetryCount = 3;
        public const int ClientRetryIntervalInSeconds = 5;
        private const string caPath = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";

        private readonly IHttpClientFactory clientFactory;



        public KubernetesClientFactory(IHttpClientFactory clientFactory)
        {
            this.clientFactory = clientFactory;
        }

        public IKubernetes CreateClient()
        {
            var config = KubernetesClientConfiguration.BuildDefaultConfig();

            Console.WriteLine($"clientFactory {this.clientFactory == null}");

            //var client = new Kubernetes(config, this.clientFactory.CreateClient("k8s"));
            var client = new Kubernetes(config, this.clientFactory.CreateClient("k8s"));

            Console.WriteLine("Append DelegatingHandler");

            var m = client.HttpMessageHandlers?.OfType<RetryDelegatingHandler>();
            var handlerNames = (m == null) ? "null" : m.Count() + " " + string.Join("", m.Select(m => m.GetType().FullName));
            Console.WriteLine($"{handlerNames}");

            return client;
        }

        public static bool ServerCertificateValidationCallback(
            HttpRequestMessage request,
            X509Certificate2 certificate,
            X509Chain certChain,
            SslPolicyErrors sslPolicyErrors)
        {

            if (certChain != null)
            {
                Console.WriteLine("chain not null");
                foreach (var status in certChain.ChainStatus)
                {
                    Console.WriteLine(status.Status);
                    Console.WriteLine(status.StatusInformation);
                    Console.WriteLine(certificate.Subject);
                    Console.WriteLine(certificate.Issuer);
                }
            }
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                // certificate is already valid
                return true;
            }
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
            {

                Console.WriteLine("RemoteCertificateChainErrors");

                // only remaining error state is RemoteCertificateChainErrors
                // check custom CA
                var privateChain = new X509Chain();
                privateChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                var caCert = new X509Certificate2(caPath);
                // https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy?view=netcore-2.2
                // Add CA cert to the chain store to include it in the chain check.
                privateChain.ChainPolicy.ExtraStore.Add(caCert);
                // Build the chain for `certificate` which should be the self-signed kubernetes api-server cert.
                privateChain.Build(certificate);

                foreach (X509ChainStatus chainStatus in privateChain.ChainStatus)
                {
                    Console.WriteLine(chainStatus.Status);
                    Console.WriteLine(chainStatus.StatusInformation);
                    if (chainStatus.Status != X509ChainStatusFlags.NoError &&
                        // root CA cert is not always trusted.
                        chainStatus.Status != X509ChainStatusFlags.UntrustedRoot)
                    {
                        return false;
                    }
                }

                return true;
            }
            else
            {
                // Unknown sslPolicyErrors
                return false;
            }
        }
    }
}
