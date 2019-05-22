using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Kudu.Services.Infrastructure.Authentication;
using Kudu.Services.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Kudu.Services.Web.Infrastructure
{
    public static class InstanceAdministrationExtensions
    {
        public static IServiceCollection AddInstanceAdminAuthentication(this IServiceCollection services)
        {
            services.AddAuthentication()
                .AddArmToken();

            return services;
        }

        public static IServiceCollection AddInstanceAdminAuthorization(this IServiceCollection services)
        {
            services.AddAuthorization(o =>
            {
                o.AddInstanceAdminPolicies();
            });

            services.AddSingleton<IAuthorizationHandler, AuthLevelAuthorizationHandler>();
            return services;
        }
    }
}
