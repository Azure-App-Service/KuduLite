using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Kudu.Core.K8SE;

namespace Kudu.Core.Kube
{

    public class SecretProvider
    {
        private readonly string _secretKubeApiUrlPlaceHolder = "https://kubernetes.default.svc.cluster.local/api/v1/namespaces/{0}/secrets/{1}";
        private readonly string _rbacServiceActTokenFilePath = "/var/run/secrets/kubernetes.io/serviceaccount/token";

        public async Task<string> GetSecretContent(string secretName, string secretNamespace)
        {
            var responseBodyContent = "";
            var secretKubeApiUrl = string.Format(_secretKubeApiUrlPlaceHolder, secretNamespace, secretName);
            var accessToken = await GetAccessToken();
            var httpClient = CreateHttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken);
            var responseMessage = await httpClient.GetAsync(secretKubeApiUrl);

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

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = KubernetesClientUtil.ServerCertificateValidationCallback
            });

            return client;
        }
    }
}
