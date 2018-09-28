using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Diagnostics;
using System.Net;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Kudu.Services.Infrastructure;
using Kudu.Services.Web.Tracing;
using Microsoft.AspNetCore.Http.Extensions;

namespace Kudu.Services.Web
{
    public class KuduWebExceptionFilter : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            LogException(context);
            var status = HttpStatusCode.InternalServerError;
            var message = string.Empty;

            var exceptionType = context.Exception.GetType();
            if (exceptionType == typeof(UnauthorizedAccessException))
            {
                message = "Unauthorized Access";
                status = HttpStatusCode.Unauthorized;
            }
            else if (exceptionType == typeof(NotImplementedException))
            {
                message = "A server error occurred.";
                status = HttpStatusCode.NotImplemented;
            }
            else if (exceptionType == typeof(System.Net.Sockets.SocketException) || exceptionType == typeof(System.Net.Http.HttpRequestException))
            {
                message = "Could not connect to the backend server";
                status = HttpStatusCode.Forbidden;
            }
            context.ExceptionHandled = true;
            var response = context.HttpContext.Response;
            response.StatusCode = (int)status;
            response.ContentType = "application/json";
            var err = "message : "+message + "\n\n stack_trace : " + context.Exception.StackTrace;
            Console.WriteLine(context.Exception.Message);
            Console.WriteLine(context.Exception.StackTrace);
            response.WriteAsync(err);
        }

        private static void LogException(ExceptionContext context)
        {
            try
            {
                var httpContext = context.HttpContext;
                var tracer = TraceServices.GetRequestTracer(httpContext);
                var error = context.Exception;

                LogErrorRequest(httpContext, error);

                if (tracer == null || tracer.TraceLevel <= TraceLevel.Off)
                {
                    return;
                }

                tracer.TraceError(error);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
        
        private static void LogErrorRequest(HttpContext httpContext, Exception ex)
        {
            OperationManager.SafeExecute(() =>
            {
                var request = httpContext.Request;
                var response = httpContext.Response;
                var requestId = (string)httpContext.Items[Constants.RequestIdHeader];
                var requestTime = (DateTime)httpContext.Items[Constants.RequestDateTimeUtc];
                var latencyInMilliseconds = (long)(DateTime.UtcNow - requestTime).TotalMilliseconds;
                KuduEventSource.Log.ApiEvent(
                    ServerConfiguration.GetApplicationName(),
                    $"OnErrorRequest {ex}",
                    new Uri(request.GetDisplayUrl()).PathAndQuery,
                    request.Method,
                    requestId,
                    response.StatusCode,
                    latencyInMilliseconds,
                    request.GetUserAgent());
            });
        }
    }
}
