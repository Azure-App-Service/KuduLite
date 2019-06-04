using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Kudu.Core.Helpers;
using Kudu.Services.Infrastructure.Authorization;

namespace Kudu.Services.Infrastructure.Authentication
{
    public class ArmAuthenticationHandler : AuthenticationHandler<ArmAuthenticationOptions>
    {
        public const string ArmTokenHeaderName = "x-ms-site-restricted-token";

        private readonly ILogger _logger;

        public ArmAuthenticationHandler(IOptionsMonitor<ArmAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
            _logger = logger.CreateLogger<ArmAuthenticationHandler>();
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            AuthenticateResult result = HandleAuthenticate();

            return Task.FromResult(result);
        }

        private AuthenticateResult HandleAuthenticate()
        {
            string token = null;
            if (!Context.Request.Headers.TryGetValue(ArmTokenHeaderName, out StringValues values))
            {
                return AuthenticateResult.NoResult();
            }

            token = values.First();

            try
            {
                if (!SimpleWebTokenHelper.ValidateToken(token, Clock))
                {
                    return AuthenticateResult.Fail("Token validation failed.");
                }

                var claims = new List<Claim>
                {
                    new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString())
                };

                var identity = new ClaimsIdentity(claims, ArmAuthenticationDefaults.AuthenticationScheme);
                return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "ARM authentication token validation failed.");
                return AuthenticateResult.Fail(exc);
            }
        }
    }
}
