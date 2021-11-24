using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Kudu.Core.Kube
{
    //This is to access the build service with auth token which will go through envoy.
    public static class AuthHelper
    {
        public static string CreateToken(string base64Key)
        {
            var ticks = DateTime.UtcNow.AddHours(1).Ticks;
            var token = Encrypt($"exp={ticks}", base64Key);
            return token;
        }

        private static string Encrypt(string plaintext, string base64Key)
        {
            byte[] key = Convert.FromBase64String(base64Key);

            using (var aes = new AesGcm(key))
            {
                byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
                var tag = new byte[AesGcm.TagByteSizes.MaxSize];
                var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
                var ciphertext = new byte[plaintextBytes.Length];

                aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

                byte[] hashKey;
                using (SHA256 sha256Hash = SHA256.Create())
                {
                    hashKey = sha256Hash.ComputeHash(key);
                }

                //This keeps the same format as the decryption in auth.go of envoy in K4apps
                return string.Format("{0}.{1}.{2}", Convert.ToBase64String(nonce), Convert.ToBase64String(ciphertext.Concat(tag).ToArray()), Convert.ToBase64String(hashKey));
            }
        }
    }
}
