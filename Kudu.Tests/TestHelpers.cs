using System;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Tests
{
    public static class TestHelpers
    {
        private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private static readonly Random Random = new Random();

        public static byte[] GenerateKeyBytes()
        {
            using (var aes = new AesManaged())
            {
                aes.GenerateKey();
                return aes.Key;
            }
        }

        public static string GenerateKeyHexString(byte[] key = null)
        {
            return BitConverter.ToString(key ?? GenerateKeyBytes()).Replace("-", string.Empty);
        }
    }
}
