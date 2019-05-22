using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Kudu.Services.Infrastructure.Authentication;

namespace Kudu.Services.Infrastructure.Authorization
{
    public static class AuthOptionsExtensions
    {
        public static void AddInstanceAdminPolicies(this AuthorizationOptions options)
        {
            options.AddPolicy(AuthPolicyNames.AdminAuthLevel, p =>
            {
                p.AddInstanceAuthenticationSchemes();
                p.AddRequirements(new AuthLevelRequirement(AuthorizationLevel.Admin));
            });
        }

        private static void AddInstanceAuthenticationSchemes(this AuthorizationPolicyBuilder builder)
        {
            builder.AuthenticationSchemes.Add(ArmAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
