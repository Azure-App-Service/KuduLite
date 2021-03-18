using Kudu.Core.Functions;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace Kudu.Tests.Core.Function
{
    public class KedaFunctionTriggersProviderTests
    {
        [Fact]
        public void DurableFunctionApp()
        {
            // Generate a zip archive with a host.json and the contents of a Durable Function app
            string zipFilePath = Path.GetTempFileName();
            using (var fileStream = File.OpenWrite(zipFilePath))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                CreateJsonFileEntry(archive, "host.json", @"{""version"":""2.0"",""extensions"":{""durableTask"":{""hubName"":""DFTest"",""storageProvider"":{""type"":""MicrosoftSQL"",""connectionStringName"":""SQLDB_Connection""}}}}");
                CreateJsonFileEntry(archive, "f1/function.json", @"{""bindings"":[{""type"":""orchestrationTrigger"",""name"":""context""}],""disabled"":false}");
                CreateJsonFileEntry(archive, "f2/function.json", @"{""bindings"":[{""type"":""entityTrigger"",""name"":""ctx""}],""disabled"":false}");
                CreateJsonFileEntry(archive, "f3/function.json", @"{""bindings"":[{""type"":""activityTrigger"",""name"":""input""}],""disabled"":false}");
                CreateJsonFileEntry(archive, "f4/function.json", @"{""bindings"":[{""type"":""httpTrigger"",""methods"":[""post""],""authLevel"":""anonymous"",""name"":""req""}],""disabled"":false}");
            }

            try
            {
                var provider = new KedaFunctionTriggerProvider();
                IEnumerable<ScaleTrigger> result = provider.GetFunctionTriggers(zipFilePath);
                Assert.Equal(2, result.Count());

                ScaleTrigger mssqlTrigger = Assert.Single(result, trigger => trigger.Type.Equals("mssql", StringComparison.OrdinalIgnoreCase));
                string connectionStringName = Assert.Contains("connectionStringFromEnv", mssqlTrigger.Metadata);
                Assert.Equal("SQLDB_Connection", connectionStringName);

                ScaleTrigger httpTrigger = Assert.Single(result, trigger => trigger.Type.Equals("httpTrigger", StringComparison.OrdinalIgnoreCase));
                string functionName = Assert.Contains("functionName", httpTrigger.Metadata);
                Assert.Equal("f4", functionName);
            }
            finally
            {
                File.Delete(zipFilePath);
            }
        }

        private static void CreateJsonFileEntry(ZipArchive archive, string path, string content)
        {
            using (Stream entryStream = archive.CreateEntry(path).Open())
            using (var streamWriter = new StreamWriter(entryStream))
            {
                streamWriter.Write(content);
            }
        }
    }
}
