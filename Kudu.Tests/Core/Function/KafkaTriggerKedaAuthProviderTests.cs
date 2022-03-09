using Kudu.Core.Functions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Moq;

namespace Kudu.Tests.Core.Function
{
    public class KafkaTriggerKedaAuthProviderTests
    {
        private const string  jsonText = @"
            {
                ""Protocol"": ""SaslSsl"",
                ""authenticationMode"": ""Plain"",
                ""username"": ""test"",
                ""password"": ""test""
            }";

         private const string  jsonTextWithoutProtocol = @"
            {
                ""authenticationMode"": ""Plain"",
                ""username"": ""test"",
                ""password"": ""test""
            }";

        private const string  jsonTextWithoutAuthMode = @"
            {
                ""protocol"": ""SaslSsl"",
                ""username"": ""test"",
                ""password"": ""test""
            }";

        [Theory]
        [InlineData(jsonText, "testFunctionName1")]
        [InlineData(jsonText, "testFunctionName2")]
        public void TestPopulateAuthenticationRef(string jsonText, string appName)
        {
            Mock<KafkaTriggerKedaAuthProvider> mock = new Mock<KafkaTriggerKedaAuthProvider>();
            mock.Setup(m => m.CreateTriggerAuthenticationRef(It.IsAny<Dictionary<string,string>>(), It.IsAny<String>())).Verifiable();
            KafkaTriggerKedaAuthProvider kafkaTriggerKedaAuthProvider = mock.Object;
            // KafkaTriggerKedaAuthProviderOverload kafkaTriggerKedaAuthProvider = new KafkaTriggerKedaAuthProviderOverload();
           // KafkaTriggerKedaAuthProvider kafkaTriggerKedaAuthProvider = new KafkaTriggerKedaAuthProvider(mock.Object);
            JToken jsonObj = JToken.Parse(jsonText);
            IDictionary<string, string> authRef = kafkaTriggerKedaAuthProvider.PopulateAuthenticationRef(jsonObj, appName);
            Assert.Equal(appName, authRef["name"]);
            mock.Verify();
        }

        [Theory]
        [InlineData(jsonTextWithoutProtocol, "testFunctionName")]
        [InlineData(jsonTextWithoutAuthMode, "testFunctionName")]
        public void TestIFTriggerAuthIsNull(string jsonData, string appName)
        {
            KafkaTriggerKedaAuthProviderOverload kafkaTriggerKedaAuthProvider = new KafkaTriggerKedaAuthProviderOverload();
            JToken jsonObj = JToken.Parse(jsonData);
            IDictionary<string, string> authRef = kafkaTriggerKedaAuthProvider.PopulateAuthenticationRef(jsonObj, appName);
            Assert.Null(authRef);
        }

        [Fact]
        public void PopulateAuthenticationRef_Fails_When_TriggerAuthCreationFails()
        {
            KafkaTriggerKedaAuthProviderCreateTriggerErrorMock kafkaTriggerKedaAuthProvider = new KafkaTriggerKedaAuthProviderCreateTriggerErrorMock();
            JToken jsonObj = JToken.Parse(jsonText);
            IDictionary<string, string> authRef = kafkaTriggerKedaAuthProvider.PopulateAuthenticationRef(jsonObj, "testFunctionName");
            Assert.Null(authRef);
        }

        [Fact]
        public void PopulateAuthenticationRef_Continues_When_AddSecretsFails()
        {
            KafkaTriggerKedaAuthProviderAppSettingsErrorMock kafkaTriggerKedaAuthProvider = new KafkaTriggerKedaAuthProviderAppSettingsErrorMock();
            JToken jsonObj = JToken.Parse(jsonText);
            IDictionary<string, string> authRef = kafkaTriggerKedaAuthProvider.PopulateAuthenticationRef(jsonObj, "testFunctionName");
            Assert.Equal("testFunctionName", authRef["name"]);
        }

        private class KafkaTriggerKedaAuthProviderOverload : KafkaTriggerKedaAuthProvider
        {
            internal override void CreateTriggerAuthenticationRef(IDictionary<string, string> secretKeyToKedaParam, string functionName)
            {
                // do nothing.
                // Avoiding running actual buildctl commands.
            }
        }

        private class KafkaTriggerKedaAuthProviderCreateTriggerErrorMock : KafkaTriggerKedaAuthProvider
        {
            internal override void CreateTriggerAuthenticationRef(IDictionary<string, string> secretKeyToKedaParam, string functionName)
            {
                throw new Exception("exception for unit test");
            }
        }

         private class KafkaTriggerKedaAuthProviderAppSettingsErrorMock : KafkaTriggerKedaAuthProvider
        {
             internal override void CreateTriggerAuthenticationRef(IDictionary<string, string> secretKeyToKedaParam, string functionName)
            {
                // do nothing.
                // Avoiding running actual buildctl commands.
            }

            internal override void AddTriggerAuthAppSettingsSecrets(IDictionary<string, string> secretsForAppSettings, string functionName)
            {
                throw new Exception("exception for unit test");
            }
        }
    }
}
