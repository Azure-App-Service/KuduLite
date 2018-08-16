﻿using Microsoft.AspNetCore.DataProtection;
using Microsoft.Azure.Web.DataProtection;
using System;
using System.Security.Cryptography;

namespace Kudu.Core.Infrastructure
{
    public static class SecurityUtility
    {
        private const string DefaultProtectorPurpose = "function-secrets";

        private static string GenerateSecretString()
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] data = new byte[40];
                rng.GetBytes(data);
                string secret = Convert.ToBase64String(data);
                // Replace pluses as they are problematic as URL values
                return secret.Replace('+', 'a');
            }
        }

        public static Tuple<string, string>[] GenerateSecretStringsKeyPair(int number)
        {
            var unencryptedToEncryptedKeyPair = new Tuple<string, string>[number];
            var protector = Microsoft.Azure.Web.DataProtection.DataProtectionProvider.CreateAzureDataProtector().CreateProtector(DefaultProtectorPurpose);
            for (int i = 0; i < number; i++)
            {
                string unencryptedKey = GenerateSecretString();
                unencryptedToEncryptedKeyPair[i] = new Tuple<string, string>(unencryptedKey, protector.Protect(unencryptedKey));
            }
            return unencryptedToEncryptedKeyPair;
        }

        public static string DecryptSecretString(string content)
        {
            try
            {
                var protector = Microsoft.Azure.Web.DataProtection.DataProtectionProvider.CreateAzureDataProtector().CreateProtector(DefaultProtectorPurpose);
                return protector.Unprotect(content);
            }
            catch (CryptographicException ex)
            {
                throw new FormatException($"unable to decrypt {content}, the key is either invalid or malformed", ex);
            }
        }

        public static string GenerateFunctionToken()
        {
            string siteName = ServerConfiguration.GetApplicationName();
            string issuer = $"https://{siteName}.scm.azurewebsites.net";
            string audience = $"https://{siteName}.azurewebsites.net/azurefunctions";
            return JwtGenerator.GenerateToken(issuer, audience, expires: DateTime.UtcNow.AddMinutes(2));
        }
    }
}

