using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.IO;

namespace Kudu.Core.Kube
{
    public class SyncTriggerAuthenticator
    {
        private const string SiteTokenHeaderName = "x-ms-site-restricted-token";
        private const string FuncAppEncryptionKeyName = "WEBSITE_AUTH_ENCRYPTION_KEY";
        private const string FuncAppNameHeaderKey = "K8SE_APP_NAME";
        private const string FuncAppNamespaceHeaderKey = "K8SE_APP_NAMESPACE";
        public async static Task<bool> AuthenticateCaller(Dictionary<string, IEnumerable<string>> headers)
        {
            if (headers == null || !headers.Any())
            {
                return false;
            }

            //If there's no encryption key in the header return true
            if (!headers.TryGetValue(SiteTokenHeaderName, out IEnumerable<string> siteTokenHeaderValue))
            {
                return false;
            }

            //Auth header value is null or empty return false
            var funcAppAuthToken = siteTokenHeaderValue.FirstOrDefault();
            if (string.IsNullOrEmpty(funcAppAuthToken))
            {
                return false;
            }

            //If there's no app name or app namespace in the header return false
            if (!headers.TryGetValue(FuncAppNameHeaderKey, out IEnumerable<string> funcAppNameHeaderValue)
                || !headers.TryGetValue(FuncAppNamespaceHeaderKey, out IEnumerable<string> funcAppNamespaceHeaderValue))
            {
                return false;
            }

            var funcAppName = funcAppNameHeaderValue.FirstOrDefault();
            var funcAppNamespace = funcAppNamespaceHeaderValue.FirstOrDefault();
            if (string.IsNullOrEmpty(funcAppName) || string.IsNullOrEmpty(funcAppNamespace))
            {
                return false;
            }

            //If the encryption key secret is null or empty in the Kubernetes - return false
            var secretProvider = new SecretProvider();
            var encryptionKeySecretContent = await secretProvider.GetSecretContent(funcAppName + "-secrets".ToLower(), funcAppNamespace);
            if (string.IsNullOrEmpty(encryptionKeySecretContent))
            {
                return false;
            }

            var encryptionSecretJObject = JObject.Parse(encryptionKeySecretContent);
            var functionEncryptionKey = Base64Decode((string)encryptionSecretJObject["data"][FuncAppEncryptionKeyName]);
            if (string.IsNullOrEmpty(functionEncryptionKey))
            {
                return false;
            }

            var decryptedToken = Decrypt(GetKeyBytes(functionEncryptionKey), funcAppAuthToken);

            return ValidateToken(decryptedToken);
        }

        public static byte[] GetKeyBytes(string hexOrBase64)
        {
            // only support 32 bytes (256 bits) key length
            if (hexOrBase64.Length == 64)
            {
                return Enumerable.Range(0, hexOrBase64.Length)
                    .Where(x => x % 2 == 0)
                    .Select(x => Convert.ToByte(hexOrBase64.Substring(x, 2), 16))
                    .ToArray();
            }

            return Convert.FromBase64String(hexOrBase64);
        }

        private static bool ValidateToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            var tokenValues = token.Split('=');
            if (tokenValues.Length < 2)
            {
                return false;
            }

            long ticksVal = 0;
            if (!long.TryParse(tokenValues[1], out ticksVal))
            {
                return false;
            }

            //The token will be valid only for the next 5 more minutes after being generated
            DateTime myDate = new DateTime(ticksVal);
            if (myDate.AddMinutes(5) < DateTime.UtcNow)
            {
                return false;
            }

            return true;
        }

        private static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = Convert.FromBase64String(base64EncodedData);
            return Encoding.UTF8.GetString(base64EncodedBytes);
        }

        private static string Decrypt(byte[] encryptionKey, string value)
        {
            var parts = value.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 && parts.Length != 3)
            {
                throw new InvalidOperationException("Malformed token.");
            }

            var iv = Convert.FromBase64String(parts[0]);
            var data = Convert.FromBase64String(parts[1]);
            var base64KeyHash = parts.Length == 3 ? parts[2] : null;

            if (!string.IsNullOrEmpty(base64KeyHash) && !string.Equals(GetSHA256Base64String(encryptionKey), base64KeyHash))
            {
                throw new InvalidOperationException(string.Format("Key with hash {0} does not exist.", base64KeyHash));
            }

            using (var aes = new AesManaged { Key = encryptionKey })
            {
                using (var ms = new MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, aes.CreateDecryptor(aes.Key, iv), CryptoStreamMode.Write))
                    using (var binaryWriter = new BinaryWriter(cs))
                    {
                        binaryWriter.Write(data, 0, data.Length);
                    }

                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }

        private static string GetSHA256Base64String(byte[] key)
        {
            using (var sha256 = new SHA256Managed())
            {
                return Convert.ToBase64String(sha256.ComputeHash(key));
            }
        }
    }
}
