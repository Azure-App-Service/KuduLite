using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Authentication;

namespace Kudu.Services.Infrastructure.Authentication
{
    public static class ArmAuthenticationExtensions
    {
        public static AuthenticationBuilder AddArmToken(this AuthenticationBuilder builder)
            => builder.AddScheme<ArmAuthenticationOptions, ArmAuthenticationHandler>(ArmAuthenticationDefaults.AuthenticationScheme, _ => { });
    }
}
