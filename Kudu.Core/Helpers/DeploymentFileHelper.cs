using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Kudu.Contracts.Tracing;
using Kudu.Core.Tracing;

namespace Kudu.Core.Helpers
{
    public class DeploymentFileHelper
    {
        private readonly string host;
        private readonly string version;
        private readonly string token;
        private readonly ITracer tracer;
        private static HttpClient httpClient = new HttpClient();

        public DeploymentFileHelper(string host, string version, string token, ITracer tracer)
        {
            this.host = host;
            this.version = version;
            this.token = token;
            this.tracer = tracer;
        }

        public async Task UploadArtifact(string filePath)
        {
            var uri = $"https://{host}/api/vfs/site/artifacts/{version}/artifact.zip";
            await UploadFile(httpClient, filePath, uri);
        }

        public async Task UploadLog(string sitePath)
        {
            var kuduTracePath = Path.Combine(sitePath, "../LogFiles/kudu/trace");
            string[] kuduTraceEntries = Directory.GetFileSystemEntries(kuduTracePath, "*", SearchOption.AllDirectories);

            foreach (var entry in kuduTraceEntries)
            {
                var kuduTraceuri = $"https://{host}/api/vfs/{entry.Replace(sitePath, "")}";
                await UploadFile(httpClient, entry, kuduTraceuri);
            }

            var logTracePath = Path.Combine(sitePath, "deployments", version);
            string[] logEntries = Directory.GetFileSystemEntries(logTracePath, "*", SearchOption.AllDirectories);
            foreach (var entry in logEntries)
            {
                var kuduTraceuri = $"https://{host}/api/vfs/site/{entry.Replace(sitePath, "")}";
                await UploadFile(httpClient, entry, kuduTraceuri);
            }
        }

        public async Task UploadCompleteFile(string sitePath)
        {
            var completeFilePath = Path.Combine(sitePath, "deployments", Constants.BuildCompleteFile);

            var logTracePath = Path.Combine(sitePath, "deployments", version);
            string[] logEntries = Directory.GetFileSystemEntries(logTracePath, "*", SearchOption.AllDirectories);
            foreach (var entry in logEntries)
            {
                var completeFileUri = $"https://{host}/api/vfs/site/deployments/{Constants.BuildCompleteFile}";
                await UploadContent(httpClient, null, completeFileUri);
            }
        }

        public async Task Download(string filePath, string uri, bool allowNotFound = false)
        {
            await DownloadFile(httpClient, filePath, uri, allowNotFound);
        }

        private async Task UploadFile(HttpClient httpClient, string filePath, string uri)
        {
            try
            {
                using (var fileStream = new FileStream(filePath,
                        FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                {
                    using (var content = new StreamContent(fileStream))
                    {
                        await UploadContent(httpClient, content, uri);
                    }
                }
            }
            catch (HttpRequestException hre)
            {
                tracer.Trace($"Failed to upload file from packageUri {uri}");
                tracer.TraceError(hre);
                throw;
            }
        }

        private async Task UploadContent(HttpClient httpClient, HttpContent content, string uri)
        {
            using (var requestMessage = CreateRequestMessage(HttpMethod.Put, uri))
            {
                if (content != null)
                {
                    requestMessage.Content = content;
                }

                requestMessage.Headers.TryAddWithoutValidation("If-Match", "*");

                using (var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                }
            }
        }

        private async Task DownloadFile(HttpClient httpClient, string filePath, string uri, bool allowNotFound = false)
        {
            using (var zipUrlRequest = CreateRequestMessage(HttpMethod.Get, uri))
            {
                using (var zipUrlResponse = await httpClient.SendAsync(zipUrlRequest))
                {

                    try
                    {
                        if (allowNotFound && zipUrlResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            return;
                        }
                        zipUrlResponse.EnsureSuccessStatusCode();
                    }
                    catch (HttpRequestException hre)
                    {
                        tracer.Trace($"Failed to get file from packageUri {uri}");
                        tracer.TraceError(hre);
                        throw;
                    }

                    using (var content = await zipUrlResponse.Content.ReadAsStreamAsync())
                    {
                        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                        {
                            await content.CopyToAsync(fileStream);
                        }
                    }
                }
            }
        }

        private HttpRequestMessage CreateRequestMessage(HttpMethod method, string uri)
        {
            var request = new HttpRequestMessage(method, uri);
            if (token != null)
            {
                request.Headers.Add("x-ms-site-restricted-token", token);
            }

            return request;
        }
    }
}
