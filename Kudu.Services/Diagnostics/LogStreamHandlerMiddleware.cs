using Kudu.Contracts.Tracing;
using Kudu.Core.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace Kudu.Services.Performance
{
    public class LogStreamHandlerMiddleware
    {
        public LogStreamHandlerMiddleware(RequestDelegate next)
        {
        }
        public Task Invoke(HttpContext context, LogStreamManager manager, ITracer tracer)
        {
            using (tracer.Step("LogStreamHandlerMiddleware.Invoke"))
            {
                if (string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    return manager.ProcessRequest(context);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return Task.CompletedTask;
                }
            }
        }
    }

    public static class LogStreamHandlerExtensions
    {
        public static IApplicationBuilder RunLogStreamHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<LogStreamHandlerMiddleware>();
        }
    }
}