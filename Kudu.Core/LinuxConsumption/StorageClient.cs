using System;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using Kudu.Core.Tracing;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.File;

namespace Kudu.Core.LinuxConsumption
{
    public class StorageClient : IStorageClient
    {
        private readonly ISystemEnvironment _environment;

        public StorageClient(ISystemEnvironment environment)
        {
            _environment = environment;
        }

        public async Task CreateFileShare(string siteName, string connectionString, string fileShareName)
        {
            try
            {
                var storageAccount = CloudStorageAccount.Parse(connectionString);
                var fileClient = storageAccount.CreateCloudFileClient();

                // Get a reference to the file share we created previously.
                CloudFileShare share = fileClient.GetShareReference(fileShareName);

                KuduEventGenerator.Log(_environment).LogMessage(EventLevel.Informational, siteName,
                    $"Creating Kudu mount file share {fileShareName}", string.Empty);

                await share.CreateIfNotExistsAsync(new FileRequestOptions(), new OperationContext());
            }
            catch (Exception e)
            {
                KuduEventGenerator.Log(_environment)
                    .LogMessage(EventLevel.Warning, siteName, nameof(CreateFileShare), e.ToString());
                throw;
            }
        }
    }
}