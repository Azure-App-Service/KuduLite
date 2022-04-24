using System;
using System.Collections.Generic;
using System.Linq;
using Kudu.Core.Functions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Kudu.Tests.Core.Function
{
    public class SyncTriggerHandlerTests
    {
        [Theory]
        [InlineData("{\"triggers\":[{\"type\":\"httpTrigger\",\"methods\":[\"get\",\"post\"],\"authLevel\":\"function\",\"name\":\"req\",\"functionName\":\"Function1 - Oct152020 - 1\"},{\"type\":\"queueTrigger\",\"connection\":\"AzureWebJobsStorage\",\"queueName\":\"myqueue - 1\",\"name\":\"myQueueItem\",\"functionName\":\"Function2\"}], \"functions\":[]}")]
        [InlineData("Invalid json")]
        [InlineData(null)]
        public void GetScaleTriggersTest(string functionTriggerPayload)
        {
            var syncTriggerHandler = new SyncTriggerHandler(null, null, null);
            var scaleTriggers = syncTriggerHandler.GetScaleTriggers("default", functionTriggerPayload);
            if (string.Equals(functionTriggerPayload, "Invalid json") || string.IsNullOrEmpty(functionTriggerPayload))
            {
                Assert.True(!string.IsNullOrEmpty(scaleTriggers.Item2));
            }
            else
            {
                Assert.True(scaleTriggers != null && scaleTriggers.Item1 != null && scaleTriggers.Item1.ToList<ScaleTrigger>().Count > 0);
            }
        }

        [Fact]
        public void DurableFunctions_SyncTriggers()
        {
            // NOTE: Same basic inputs as KedaFunctionTriggersProviderTests.DurableFunctionsApp
            JObject syncTriggersPayloadJson = new JObject(
                new JProperty("extensions", JObject.Parse(@"{""durableTask"":{""hubName"":""DFTest"",""storageProvider"":{""type"":""mssql"",""connectionStringName"":""SQLDB_Connection""}}}")),
                new JProperty("triggers", new JArray(
                    JObject.Parse(@"{""functionName"":""f1"",""type"":""orchestrationTrigger"",""name"":""context""}"),
                    JObject.Parse(@"{""functionName"":""f2"",""type"":""entityTrigger"",""name"":""ctx""}"),
                    JObject.Parse(@"{""functionName"":""f3"",""type"":""activityTrigger"",""name"":""input""}"),
                    JObject.Parse(@"{""functionName"":""f4"",""type"":""queueTrigger"",""connection"":""AzureWebjobsStorage"",""queueName"":""queue"",""name"":""queueItem""}"),
                    JObject.Parse(@"{""functionName"":""f5"",""type"":""httpTrigger"",""methods"":[""post""],""authLevel"":""anonymous"",""name"":""req""}"))));

            string serializedPayload = syncTriggersPayloadJson.ToString(Formatting.None);

            var syncTriggerHandler = new SyncTriggerHandler(null, null, null);
            (IEnumerable<ScaleTrigger> triggers, string error) = syncTriggerHandler.GetScaleTriggers("default", serializedPayload);
            Assert.Null(error);
            Assert.NotNull(triggers);

            // We expect the same output as the other Durable Functions test
            KedaFunctionTriggersProviderTests.ValidateDurableTriggers(triggers);
        }
    }
}
