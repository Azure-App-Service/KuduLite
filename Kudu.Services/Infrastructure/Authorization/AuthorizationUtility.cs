using System;
using System.Linq;
using System.Security.Claims;
using Kudu.Services.Infrastructure.Authentication;

namespace Kudu.Services.Infrastructure.Authorization
{
    public class AuthorizationUtility
    {
        /// <summary>
        /// Test if a specific principle matches the authorization requirement
        /// </summary>
        /// <param name="principal">The current pricipal of a request</param>
        /// <param name="requiredLevel">The required level for performing an action</param>
        /// <param name="keyName">The name of the authorization key</param>
        /// <returns>True on access granted</returns>
        public static bool PrincipalHasAuthLevelClaim(ClaimsPrincipal principal, AuthorizationLevel requiredLevel, string keyName = null)
        {
            // If the required auth level is anonymous, the requirement is met
            if (requiredLevel == AuthorizationLevel.Anonymous)
            {
                return true;
            }

            var claimLevels = principal
                .FindAll(SecurityConstants.AuthLevelClaimType)
                .Select(c => Enum.TryParse(c.Value, out AuthorizationLevel claimLevel) ? claimLevel : AuthorizationLevel.Anonymous)
                .ToArray();

            if (claimLevels.Length > 0)
            {
                // If we have a claim with Admin level, regardless of whether a name is required, return true.
                if (claimLevels.Any(claimLevel => claimLevel == AuthorizationLevel.Admin))
                {
                    return true;
                }

                // Ensure we match the expected level and key name, if one is required
                if (claimLevels.Any(l => l == requiredLevel) &&
                   (keyName == null || string.Equals(principal.FindFirstValue(SecurityConstants.AuthLevelKeyNameClaimType), keyName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
