using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Services.Infrastructure.Authentication
{
    public class SecurityConstants
    {
        public const string AuthLevelClaimType = "http://schemas.microsoft.com/2017/07/functions/claims/authlevel";
        public const string AuthLevelKeyNameClaimType = "http://schemas.microsoft.com/2017/07/functions/claims/keyid";
    }
}
