using System;
using Microsoft.AspNetCore.Authentication;
using Xunit;
using Kudu.Core.Helpers;
using Kudu.Contracts.Settings;

namespace Kudu.Tests.Core.Helpers
{
    public class SimpleWebTokenTests
    {
        [Fact]
        public void EncryptShouldThrowIdNoEncryptionKeyDefined()
        {
            // Make sure WEBSITE_AUTH_ENCRYPTION_KEY is empty
            using (new TestScopedEnvironmentVariable(SettingsKeys.AuthEncryptionKey, string.Empty))
            {
                try
                {
                    SimpleWebTokenHelper.Encrypt("value");
                }
                catch (Exception ex)
                {
                    Assert.IsType<InvalidOperationException>(ex);
                    Assert.Contains(SettingsKeys.AuthEncryptionKey, ex.Message);
                }
            }
        }

        [Theory]
        [InlineData("value")]
        public void EncryptShouldGenerateDecryptableValues(string valueToEncrypt)
        {
            var key = TestHelpers.GenerateKeyBytes();
            var stringKey = TestHelpers.GenerateKeyHexString(key);

            using (new TestScopedEnvironmentVariable(SettingsKeys.AuthEncryptionKey, stringKey))
            {
                var encrypted = SimpleWebTokenHelper.Encrypt(valueToEncrypt);
                var decrypted = SimpleWebTokenHelper.Decrypt(key, encrypted);
                Assert.Matches("(.*)[.](.*)[.](.*)", encrypted);
                Assert.Equal(valueToEncrypt, decrypted);
            }
        }

        [Fact]
        public void CreateTokenShouldCreateAValidToken()
        {
            var key = TestHelpers.GenerateKeyBytes();
            var stringKey = TestHelpers.GenerateKeyHexString(key);
            var timeStamp = DateTime.UtcNow;

            using (new TestScopedEnvironmentVariable(SettingsKeys.AuthEncryptionKey, stringKey))
            {
                var token = SimpleWebTokenHelper.CreateToken(timeStamp);
                var decrypted = SimpleWebTokenHelper.Decrypt(key, token);

                Assert.Equal($"exp={timeStamp.Ticks}", decrypted);
            }
        }

        [Fact]
        public void Validate_Token_Uses_WebSiteAuthEncryptionKey_If_Available()
        {
            var containerEncryptionKey = TestHelpers.GenerateKeyBytes();
            var containerEncryptionStringKey = TestHelpers.GenerateKeyHexString(containerEncryptionKey);

            var websiteAuthEncryptionKey = TestHelpers.GenerateKeyBytes();
            var websiteAuthEncryptionStringKey = TestHelpers.GenerateKeyHexString(websiteAuthEncryptionKey);

            var timeStamp = DateTime.UtcNow.AddHours(1);

            using (new TestScopedEnvironmentVariable(SettingsKeys.ContainerEncryptionKey, containerEncryptionStringKey))
            using (new TestScopedEnvironmentVariable(SettingsKeys.AuthEncryptionKey, websiteAuthEncryptionStringKey))
            {
                var token = SimpleWebTokenHelper.CreateToken(timeStamp, websiteAuthEncryptionKey);
                Assert.True(SimpleWebTokenHelper.TryValidateToken(token, new SystemClock()));
            }

        }

        [Fact]
        public void Validate_Token_Uses_Website_Encryption_Key_If_Container_Encryption_Key_Not_Available()
        {
            var websiteAuthEncryptionKey = TestHelpers.GenerateKeyBytes();
            var websiteAuthEncryptionStringKey = TestHelpers.GenerateKeyHexString(websiteAuthEncryptionKey);

            var timeStamp = DateTime.UtcNow.AddHours(1);

            using (new TestScopedEnvironmentVariable(SettingsKeys.ContainerEncryptionKey, string.Empty))
            using (new TestScopedEnvironmentVariable(SettingsKeys.AuthEncryptionKey, websiteAuthEncryptionStringKey))
            {
                var token = SimpleWebTokenHelper.CreateToken(timeStamp, websiteAuthEncryptionKey);
                Assert.True(SimpleWebTokenHelper.TryValidateToken(token, new SystemClock()));
            }
        }
    }
}
