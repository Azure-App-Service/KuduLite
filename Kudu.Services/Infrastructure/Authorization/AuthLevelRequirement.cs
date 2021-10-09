using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Authorization;

namespace Kudu.Services.Infrastructure.Authorization
{
    public class AuthLevelRequirement : IAuthorizationRequirement
    {
        public AuthLevelRequirement(AuthorizationLevel level)
        {
            Level = level;
        }

        public AuthorizationLevel Level { get; }
    }
}
