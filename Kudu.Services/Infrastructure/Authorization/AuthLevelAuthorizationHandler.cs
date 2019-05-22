using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Kudu.Services.Infrastructure.Authorization
{
    public class AuthLevelAuthorizationHandler : AuthorizationHandler<AuthLevelRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AuthLevelRequirement requirement)
        {
            if (AuthorizationUtility.PrincipalHasAuthLevelClaim(context.User, requirement.Level))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
