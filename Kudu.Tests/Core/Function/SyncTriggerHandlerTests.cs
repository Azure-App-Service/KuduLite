using Kudu.Core.Functions;
using System.Linq;
using Xunit;

namespace Kudu.Tests.Core.Function
{
    public class SyncTriggerHandlerTests
    {
        [Theory]
        [InlineData("[{\"type\":\"httpTrigger\",\"methods\":[\"get\",\"post\"],\"authLevel\":\"function\",\"name\":\"req\",\"functionName\":\"Function1 - Oct152020 - 1\"},{\"type\":\"queueTrigger\",\"connection\":\"AzureWebJobsStorage\",\"queueName\":\"myqueue - 1\",\"name\":\"myQueueItem\",\"functionName\":\"Function2\"}]")]
        [InlineData("Invalid json")]
        [InlineData(null)]
        public void GetScaleTriggersTest(string functionTriggerPayload)
        {
            var syncTriggerHandler = new SyncTriggerHandler(null, null, null);
            var scaleTriggers = syncTriggerHandler.GetScaleTriggers(functionTriggerPayload);
            if (string.Equals(functionTriggerPayload, "Invalid json") || string.IsNullOrEmpty(functionTriggerPayload))
            {
                Assert.True(!string.IsNullOrEmpty(scaleTriggers.Item2));
            }
            else
            {
                Assert.True(scaleTriggers != null && scaleTriggers.Item1 != null && scaleTriggers.Item1.ToList<ScaleTrigger>().Count > 0);
            }
        }
    }
}
