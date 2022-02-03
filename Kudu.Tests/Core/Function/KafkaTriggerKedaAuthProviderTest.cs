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
        public void TestPopulateAuthenticationRef_Is_BindingCaseInsensitive()
        {
            KafkaTriggerKedaAuthProviderOverload kafkaTriggerKedaAuthProvider = new KafkaTriggerKedaAuthProviderOverload();
            string jsonText = @"
            {
                ""Protocol"": ""SaslSsl"",
                ""authenticationMode"": ""Plain"",
                ""username"": ""test"",
                ""password"": ""test""
            }";

            JToken jsonObj = JToken.Parse(jsonText);
            IDictionary<string, string> authRef = kafkaTriggerKedaAuthProvider.PopulateAuthenticationRef(jsonObj, "testFunctionName");
            Assert.Equal(1, authRef.Count);
        }

        [Fact]
        public void PopulateAuthenticationRef_With_ProtocolData()
        {
            KafkaTriggerKedaAuthProviderOverload kafkaTriggerKedaAuthProvider = new KafkaTriggerKedaAuthProviderOverload();
            string jsonText = @"
            {
                ""protocol"": ""SaslSsl"",
                ""authenticationMode"": ""Plain"",
                ""username"": ""test"",
                ""password"": ""test""
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
                ""protocol"": ""SaslSsl"",
                ""authenticationMode"": ""Plain"",
                ""username"": ""test"",
                ""password"": ""test""
            }";

            JToken jsonObj = JToken.Parse(jsonText);
            IDictionary<string, string> authRef = kafkaTriggerKedaAuthProvider.PopulateAuthenticationRef(jsonObj, "testFunctionName");
            Assert.Null(authRef);
        }

        [Fact]
        public void PopulateAuthenticationRef_Continues_When_AddSecretsFails()
        {
            KafkaTriggerKedaAuthProviderErrorMock kafkaTriggerKedaAuthProvider = new KafkaTriggerKedaAuthProviderErrorMock();
            string jsonText = @"
            {
                ""protocol"": ""SaslSsl"",
                ""authenticationMode"": ""Plain"",
                ""username"": ""test"",
                ""password"": ""test""
            }";

            JToken jsonObj = JToken.Parse(jsonText);
            IDictionary<string, string> authRef = kafkaTriggerKedaAuthProvider.PopulateAuthenticationRef(jsonObj, "testFunctionName");
            Assert.Equal(1, authRef.Count);
        }

        [Fact]
        public void TestIFTriggerAuthIsNull_With_NoAuthenticationMode()
        {
            KafkaTriggerKedaAuthProviderOverload kafkaTriggerKedaAuthProvider = new KafkaTriggerKedaAuthProviderOverload();
            string jsonText = @"
            {
                ""protocol"": ""SaslSsl"",
                ""username"": ""test"",
                ""password"": ""test""
            }";

            JToken jsonObj = JToken.Parse(jsonText);
            IDictionary<string, string> authRef = kafkaTriggerKedaAuthProvider.PopulateAuthenticationRef(jsonObj, "testFunctionName");
            Assert.Null(authRef);
        }

        [Fact]
         public void TestIFTriggerAuthIsNull_With_NoProtocol()
        {
            KafkaTriggerKedaAuthProviderOverload kafkaTriggerKedaAuthProvider = new KafkaTriggerKedaAuthProviderOverload();
            string jsonText = @"
            {
                ""authenticationMode"": ""Plain"",
                ""username"": ""test"",
                ""password"": ""test""
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

            internal override void AddTriggerAuthAppSettingsSecrets(IDictionary<string, string> secretsForAppSettings, string functionName)
            {
                throw new Exception("exception for unit test");
            }
        }
    }
}
