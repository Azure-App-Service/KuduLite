using System.Security.Claims;
using System.Linq;
using Kudu.Services.Infrastructure.Authentication;
using Kudu.Services.Infrastructure.Authorization;
using Xunit;

namespace Kudu.Tests.Services.Infrastructure.Authorization
{
    public class AuthorizationUtilityTests
    {
        [Theory]
        [InlineData(new[] { AuthorizationLevel.Admin }, AuthorizationLevel.Admin, true)]
        [InlineData(new[] { AuthorizationLevel.Admin }, AuthorizationLevel.Anonymous, true)]
        [InlineData(new[] { AuthorizationLevel.Anonymous }, AuthorizationLevel.Anonymous, true)]
        [InlineData(new[] { AuthorizationLevel.Anonymous }, AuthorizationLevel.Admin, false)]
        public void ClaimedPrincipalAuthorizationTests(AuthorizationLevel[] principalLevel, AuthorizationLevel requiredLevel, bool expectSuccess)
        {
            ClaimsPrincipal principal = CreatePrincipal(principalLevel);
            bool result = AuthorizationUtility.PrincipalHasAuthLevelClaim(principal, requiredLevel);

            Assert.Equal(expectSuccess, result);
        }

        private ClaimsPrincipal CreatePrincipal(AuthorizationLevel[] levels)
        {
            var claims = levels.Select(l => new Claim(SecurityConstants.AuthLevelClaimType, l.ToString()));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }
    }
}
