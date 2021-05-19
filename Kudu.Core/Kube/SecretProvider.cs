using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Kudu.Core.Kube
{

    public class SecretProvider
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _secretKubeApiUrlPlaceHolder = "https://kubernetes.default.svc.cluster.local/api/v1/namespaces/{0}/secrets/{1}";
        private readonly string _rbacServiceActTokenFilePath = "/var/run/secrets/kubernetes.io/serviceaccount/token";
        private const string _caFilePath = "/var/run/secrets/kubernetes.io/serviceaccount/ca.crt";

        public async Task<string> GetSecretContent(string secretName, string secretNamespace)
        {
            var responseBodyContent = "";
            var secretKubeApiUrl = string.Format(_secretKubeApiUrlPlaceHolder, secretNamespace, secretName);
            var accessToken = await GetAccessToken();
            _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken);
            var responseMessage = await _httpClient.GetAsync(secretKubeApiUrl);

            if (responseMessage.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return responseBodyContent;
            }

            using (var reader = new StreamReader(await responseMessage.Content.ReadAsStreamAsync()))
            {
                responseBodyContent = await reader.ReadToEndAsync();
            }

            return responseBodyContent;
        }

        private async Task<string> GetAccessToken()
        {
            var accessToken = "";
            using (var sr = File.OpenText(_rbacServiceActTokenFilePath))
            {
                accessToken = await sr.ReadToEndAsync();
            }

            return accessToken;
        }

        private static bool ServerCertificateValidationCallback(
            HttpRequestMessage request,
            X509Certificate2 certificate,
            X509Chain certChain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                // certificate is already valid
                return true;
            }
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateNameMismatch ||
                sslPolicyErrors == SslPolicyErrors.RemoteCertificateNotAvailable)
            {
                // api-server cert must exist and have the right subject
                return false;
            }
            else if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
            {
                // only remaining error state is RemoteCertificateChainErrors
                // check custom CA
                var privateChain = new X509Chain();
                privateChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                var caCert = new X509Certificate2(_caFilePath);
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
