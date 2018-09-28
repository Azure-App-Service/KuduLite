using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Kudu.Services.Infrastructure;
using Kudu.Services.Web.Tracing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Kudu.Services.Web
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate next;
        private readonly ILogger<ErrorHandlingMiddleware> _logger;


        public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
        {
            this.next = next;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context /* other dependencies */)
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                //await HandleExceptionAsync(context, ex);
            }
        }

        private Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var code = HttpStatusCode.InternalServerError; // 500 if unexpected
            var status = HttpStatusCode.InternalServerError;
            var message = string.Empty;

            var exceptionType = exception.GetType();
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
            var result = JsonConvert.SerializeObject(new { error = exception.Message });
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)status;
            LogException(context, exception);
            return context.Response.WriteAsync(result);
        }
        
        private void LogException(HttpContext httpContext, Exception exception)
        {
            try
            {
                var tracer = TraceServices.GetRequestTracer(httpContext);
                var error = exception;

                //LogErrorRequest(httpContext, error);
                _logger.LogCritical(exception.Message, exception);

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