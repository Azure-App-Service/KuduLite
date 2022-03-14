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
        private const string kafkaBindings = @"
            {
                ""Protocol"": ""SaslSsl"",
                ""authenticationMode"": ""Plain"",
                ""username"": ""test"",
                ""password"": ""test""
            }";

        private const string kafkaBindingsWithoutProtocol = @"
            {
                ""authenticationMode"": ""Plain"",
                ""username"": ""test"",
                ""password"": ""test""
            }";

        private const string kafkaBindingsWithoutAuthMode = @"
            {
                ""protocol"": ""SaslSsl"",
                ""username"": ""test"",
                ""password"": ""test""
            }";

        [Theory]
        [InlineData(kafkaBindings, "testFunctionName1")]
        [InlineData(kafkaBindings, "testFunctionName2")]
        public void TestPopulateAuthenticationRef(string kafkaBindings, string appName)
        {
            Mock<KafkaTriggerKedaAuthProvider> mock = new Mock<KafkaTriggerKedaAuthProvider>();
            mock.Setup(m => m.CreateTriggerAuthenticationRef(It.IsAny<Dictionary<string, string>>(), It.IsAny<String>())).Verifiable();

            IDictionary<string, string> authRef = getAuthRef(mock, kafkaBindings, appName);
            Assert.Equal(appName, authRef["name"]);
            mock.Verify();
        }

        [Theory]
        [InlineData(kafkaBindingsWithoutProtocol, "testFunctionName")]
        [InlineData(kafkaBindingsWithoutAuthMode, "testFunctionName")]
        public void TestIfTriggerAuthIsNull(string kafkaBindings, string appName)
        {
            Mock<KafkaTriggerKedaAuthProvider> mock = new Mock<KafkaTriggerKedaAuthProvider>();
            mock.Setup(m => m.CreateTriggerAuthenticationRef(It.IsAny<Dictionary<string, string>>(), It.IsAny<String>())).Verifiable();

            IDictionary<string, string> authRef = getAuthRef(mock, kafkaBindings, appName);
            Assert.Null(authRef);
        }

        [Fact]
        public void PopulateAuthenticationRef_Fails_When_TriggerAuthCreationFails()
        {
            Mock<KafkaTriggerKedaAuthProvider> mock = new Mock<KafkaTriggerKedaAuthProvider>();
            mock.Setup(m => m.CreateTriggerAuthenticationRef(It.IsAny<Dictionary<string, string>>(), It.IsAny<String>())).Throws(new Exception("exception in trigger auth creation"));

            IDictionary<string, string> authRef = getAuthRef(mock, kafkaBindings, "testfunctionName");
            Assert.Null(authRef);
        }

        [Fact]
        public void PopulateAuthenticationRef_Continues_When_AddSecretsFails()
        {
            Mock<KafkaTriggerKedaAuthProvider> mock = new Mock<KafkaTriggerKedaAuthProvider>();
            mock.Setup(m => m.CreateTriggerAuthenticationRef(It.IsAny<Dictionary<string, string>>(), It.IsAny<String>())).Verifiable();
            mock.Setup(m => m.AddTriggerAuthAppSettingsSecrets(It.IsAny<Dictionary<string, string>>(), It.IsAny<String>())).Throws(new Exception("exception for unit test"));

            IDictionary<string, string> authRef = getAuthRef(mock, kafkaBindings, "testFunctionName");
            Assert.Equal("testFunctionName", authRef["name"]);
        }

        private IDictionary<string, string> getAuthRef(Mock<KafkaTriggerKedaAuthProvider> mock, string kafkaBindings, string appName)
        {
            KafkaTriggerKedaAuthProvider kafkaTriggerKedaAuthProvider = mock.Object;
            JToken jsonObj = JToken.Parse(kafkaBindings);
            return kafkaTriggerKedaAuthProvider.PopulateAuthenticationRef(jsonObj, appName);
        }
    }   
}
