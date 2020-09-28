using System.Threading.Tasks;
using Kudu.Contracts;
using Microsoft.AspNetCore.Http;

namespace Kudu.Services.LinuxConsumptionInstanceAdmin
{
    /// <summary>
    /// Middleware used in Linux consumption to delay requests until specialization is complete.
    /// </summary>
    public class EnvironmentReadyCheckMiddleware
    {
        private readonly RequestDelegate _next;

        public EnvironmentReadyCheckMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext, ILinuxConsumptionEnvironment environment)
        {
            if (environment.DelayRequestsEnabled)
            {
                await environment.DelayCompletionTask;
            }

            await _next.Invoke(httpContext);
        }
    }
}
