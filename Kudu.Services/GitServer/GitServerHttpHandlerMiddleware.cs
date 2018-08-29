using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Tracing;
using Kudu.Core.Deployment;
using Kudu.Core.SourceControl.Git;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace Kudu.Services.GitServer
{
    public class GitServerHttpHandlerMiddleware
    {

        protected IGitServer GitServer { get; private set; }
        protected ITracer Tracer { get; private set; }
        protected IOperationLock DeploymentLock { get; private set; }
        protected IDeploymentManager DeploymentManager { get; private set; }
        public GitServerHttpHandlerMiddleware(RequestDelegate next)
        {
            // next is never used, this middleware is always terminal
        }

        public async Task Invoke(
            HttpContext context,
            ITracer tracer,
            IGitServer gitServer,
            IDictionary<string, IOperationLock> namedLocks,
            IDeploymentManager deploymentManager)
        {
            GitServer = gitServer;
            Tracer = tracer;
            DeploymentManager = deploymentManager;
            var deploymentLock = namedLocks["deployment"];
        }

        public virtual bool IsReusable
        {
            get
            {
                return false;
            }
        }

        public static void UpdateNoCacheForResponse(HttpResponse response)
        {
            // CORE TODO no longer exists
            //response.Buffer = false;
            //response.BufferOutput = false;

            response.Headers["Expires"] = "Fri, 01 Jan 1980 00:00:00 GMT";
            response.Headers["Pragma"] = "no-cache";
            response.Headers["Cache-Control"] = "no-cache, max-age=0, must-revalidate";
        }
    }

    public static class GitServerHttpHandlerMiddlewareExtensions
    {
        public static IApplicationBuilder RunServerHttpHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<GitServerHttpHandlerMiddleware>();
        }
    }
}
