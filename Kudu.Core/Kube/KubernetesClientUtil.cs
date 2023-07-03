using System;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Rest.TransientFaultHandling;

namespace Kudu.Core.Kube
{
    public class KubernetesClientUtil
    {
        public const int ClientRetryCount = 3;
        public const int ClientRetryIntervalInSeconds = 5;
        private const string caPath = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";
        private const string serviceCAPath = "/var/run/secrets/kubernetes.io/serviceaccount/service-ca.crt";

        public static void ExecuteWithRetry(Action action)
        {
            var retryPolicy = new RetryPolicy(
                new HttpTransientErrorDetectionStrategy(),
                3,
                TimeSpan.FromSeconds(3));
            retryPolicy.ExecuteAction(action);
        }

        public static bool ServerCertificateValidationCallback(
            HttpRequestMessage request,
            X509Certificate2 certificate,
            X509Chain certChain,
            SslPolicyErrors sslPolicyErrors)
        {
            Console.WriteLine($"sslPolicyErrors: {sslPolicyErrors}");
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                // certificate is already valid
                return true;
            }
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
            {
                // only remaining error state is RemoteCertificateChainErrors
                // check custom CA
                bool caresult = true;
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
                    if (chainStatus.Status != X509ChainStatusFlags.NoError &&
                        // root CA cert is not always trusted.
                        chainStatus.Status != X509ChainStatusFlags.UntrustedRoot)
                    {
                        Console.WriteLine($"ca crt: {chainStatus.Status}");
                        caresult = false;
                        break;
                    }
                }

                if (caresult)
                {
                    return true;
                }

                if (File.Exists(serviceCAPath))
                {
                    var serviceCAprivateChain = new X509Chain();
                    serviceCAprivateChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                    var serviceCA = new X509Certificate2(serviceCAPath);
                    // https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509chainpolicy?view=netcore-2.2
                    // Add CA cert to the chain store to include it in the chain check.
                    serviceCAprivateChain.ChainPolicy.ExtraStore.Add(serviceCA);
                    // Build the chain for `certificate` which should be the self-signed kubernetes api-server cert.
                    serviceCAprivateChain.Build(certificate);

                    foreach (X509ChainStatus chainStatus in serviceCAprivateChain.ChainStatus)
                    {
                        if (chainStatus.Status != X509ChainStatusFlags.NoError &&
                            // root CA cert is not always trusted.
                            chainStatus.Status != X509ChainStatusFlags.UntrustedRoot)
                        {
                            Console.WriteLine($"service crt: {chainStatus.Status} ");
                            return false;
                        }
                    }

                    return true;
                }

                return false;
            }
            else
            {
                // Unknown sslPolicyErrors
                return false;
            }
        }
    }
}
