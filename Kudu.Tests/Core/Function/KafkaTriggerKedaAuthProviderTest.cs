using Kudu.Core.Functions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Kudu.Tests.Core.Function
{
    public class KafkaTriggerKedaAuthProviderTest
    {
        [Fact]
        public void PopulateAuthenticationRef_With_ProtocolData()
        {
            KafkaTriggerKedaAuthProviderOverload kafkaTriggerKedaAuthProvider = new KafkaTriggerKedaAuthProviderOverload();
            string jsonText = @"
            {
                ""Protocol"": ""SASL_SSL"",
                ""AuthenticationMode"": ""PLAINTEXT"",
                ""Username"": ""test"",
                ""Password"": ""test""
            }";

            JToken jsonObj = JToken.Parse(jsonText);
            IDictionary<string, string> authRef = kafkaTriggerKedaAuthProvider.PopulateAuthenticationRef(jsonObj, "testFunctionName");
            Assert.Equal(1, authRef.Count);
        }

        [Fact]
        public void PopulateAuthenticationRef_Fails_When_TriggerAuthCreationFails()
        {
            KafkaTriggerKedaAuthProviderErrorMock kafkaTriggerKedaAuthProvider = new KafkaTriggerKedaAuthProviderErrorMock();
            string jsonText = @"
            {
                ""Protocol"": ""SASL_SSL"",
                ""AuthenticationMode"": ""PLAINTEXT"",
                ""Username"": ""test"",
                ""Password"": ""test""
            }";

            JToken jsonObj = JToken.Parse(jsonText);
            IDictionary<string, string> authRef = kafkaTriggerKedaAuthProvider.PopulateAuthenticationRef(jsonObj, "testFunctionName");
            Assert.Null(authRef);
        }

        private class KafkaTriggerKedaAuthProviderOverload : KafkaTriggerKedaAuthProvider
        {
            internal override void CreateTriggerAuthenticationRef(IDictionary<string, string> secretKeyToKedaParam, string functionName)
            {
                // do nothing.
                // Avoiding running actual buildctl commands.
            }
        }

        private class KafkaTriggerKedaAuthProviderErrorMock : KafkaTriggerKedaAuthProvider
        {
            internal override void CreateTriggerAuthenticationRef(IDictionary<string, string> secretKeyToKedaParam, string functionName)
            {
                throw new Exception("exception for unit test");
            }
        }
    }
}
