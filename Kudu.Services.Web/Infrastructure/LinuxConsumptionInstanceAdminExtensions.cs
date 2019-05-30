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
    public static class LinuxConsumptionInstanceAdminExtensions
    {
        /// <summary>
        /// In Linux consumption, we are running the KuduLite instance in a Service Fabric Mesh container.
        /// We want to introduce an Antares Datarole in authentication.
        /// </summary>
        /// <param name="services">Dependency injection to application service</param>
        /// <returns>Service</returns>
        public static IServiceCollection AddLinuxConsumptionAuthentication(this IServiceCollection services)
        {
            services.AddAuthentication()
                .AddArmToken();

            return services;
        }

        /// <summary>
        /// In Linux consumption, we are running the KuduLite instance in a Service Fabric Mesh container.
        /// We want to introduce AdminAuthLevel policy to restrict instance admin endpoint access.
        /// </summary>
        /// <param name="services">Dependency injection to application service</param>
        /// <returns>Service</returns>
        public static IServiceCollection AddLinuxConsumptionAuthorization(this IServiceCollection services)
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
