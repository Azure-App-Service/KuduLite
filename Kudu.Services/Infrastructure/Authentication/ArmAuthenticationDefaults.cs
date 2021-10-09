using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Services.Infrastructure.Authentication
{
    /// <summary>
    /// Azure Resource Manager authentication scheme for Linux Consumption authentication
    /// </summary>
    public static class ArmAuthenticationDefaults
    {
        /// <summary>
        /// Use Azure Resource Manager token to authenticate (e.g. "x-ms-site-restricted-token")
        /// </summary>
        public const string AuthenticationScheme = "ArmToken";
    }
}
