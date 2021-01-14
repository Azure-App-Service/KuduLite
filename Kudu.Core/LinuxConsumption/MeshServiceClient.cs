using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Microsoft.WindowsAzure.Storage;

namespace Kudu.Core.LinuxConsumption
{
    public class MeshServiceClient : IMeshServiceClient
    {
        private readonly ISystemEnvironment _environment;
        private readonly HttpClient _client;
        private const string Operation = "operation";

        public MeshServiceClient(ISystemEnvironment environment, HttpClient client)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task MountCifs(string connectionString, string contentShare, string targetPath)
        {
            var sa = CloudStorageAccount.Parse(connectionString);
            var key = Convert.ToBase64String(sa.Credentials.ExportKey());
            var response = await SendAsync(new[]
            {
                new KeyValuePair<string, string>(Operation, "cifs"),
                new KeyValuePair<string, string>("host", sa.FileEndpoint.Host),
                new KeyValuePair<string, string>("accountName", sa.Credentials.AccountName),
                new KeyValuePair<string, string>("accountKey", key),
                new KeyValuePair<string, string>("contentShare", contentShare),
                new KeyValuePair<string, string>("targetPath", targetPath),
            });

            response.EnsureSuccessStatusCode();
        }

        private async Task<HttpResponseMessage> SendAsync(IEnumerable<KeyValuePair<string, string>> formData)
        {
            var operationName = formData.FirstOrDefault(f => string.Equals(f.Key, Operation)).Value;
            var meshUri = _environment.GetEnvironmentVariable(Constants.MeshInitURI);

            KuduEventGenerator.Log(_environment).GenericEvent(ServerConfiguration.GetApplicationName(),
                $"Sending mesh request {operationName} to {meshUri}", string.Empty, string.Empty, string.Empty, string.Empty);

            var res = await _client.PostAsync(meshUri, new FormUrlEncodedContent(formData));

            KuduEventGenerator.Log(_environment).GenericEvent(ServerConfiguration.GetApplicationName(),
                $"Mesh response {res.StatusCode}", string.Empty, string.Empty, string.Empty, string.Empty);
            return res;
        }
    }
}