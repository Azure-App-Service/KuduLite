using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kudu.Core.Helpers;

namespace Kudu.Services.Models
{
    public class EncryptedHostAssignmentContext
    {
        [JsonProperty("encryptedContext")]
        public string EncryptedContext { get; set; }

        public static EncryptedHostAssignmentContext Create(HostAssignmentContext context, string key)
        {
            string json = JsonConvert.SerializeObject(context);
            var encryptionKey = Convert.FromBase64String(key);
            string encrypted = SimpleWebTokenHelper.Encrypt(json, encryptionKey);

            return new EncryptedHostAssignmentContext { EncryptedContext = encrypted };
        }

        public HostAssignmentContext Decrypt(string key)
        {
            var decrypted = SimpleWebTokenHelper.Decrypt(key.ToKeyBytes(), EncryptedContext);
            return JsonConvert.DeserializeObject<HostAssignmentContext>(decrypted);
        }
    }
}
