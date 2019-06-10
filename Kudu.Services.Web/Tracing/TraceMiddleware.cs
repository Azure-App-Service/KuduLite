using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Kudu.Services.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Environment = Kudu.Core.Environment;

namespace Kudu.Services.Web.Tracing
{
    public class TraceMiddleware
    {
        private static readonly object _stepKey = new object();
        private static int _traceStartup;

        private static readonly DateTime _startDateTime = DateTime.UtcNow;
        private static DateTime _lastRequestDateTime;

        private static DateTime _nextHeartbeatDateTime = DateTime.MinValue;

        private readonly RequestDelegate _next;

        private readonly IKuduEventGenerator _kuduEventGenerator;

        private const string KuduLiteTrackingHeader = "X-KUDULITE-RESPONSE";

        private static readonly Lazy<string> KuduVersion = new Lazy<string>(() =>
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            return fvi.FileVersion;
        });

        public TraceMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        { 
            BeginRequest(context);
            try
            {
                await _next.Invoke(context);
            }
            catch (Exception ex)
            {
                await Task.Run(() => LogException(context, ex));
                ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
            }
            // At the end of the pipe
            EndRequest(context);
        }

        private static void AddTrackingHeader(HttpContext context)
        {
            context.Response.Headers.Add(KuduLiteTrackingHeader,"true");
        }

        private void LogException(HttpContext httpContext, Exception exception)
        {
            try
            {
                var tracer = TraceServices.GetRequestTracer(httpContext);
                Console.WriteLine(@"Exception Message : " + exception.Message);
                Console.WriteLine(@"Exception StackTrace : " + exception.StackTrace);
                tracer.TraceError(exception);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            //return httpContext.Response.WriteAsync("Test Exception");
        }

        private void BeginRequest(HttpContext httpContext)
        {
            var httpRequest = httpContext.Request; 
           
            _lastRequestDateTime = DateTime.UtcNow;

            /* CORE TODO missing functionality:
             * Disallow GET requests from CSM extensions bridge
             * Razor dummy extension for vfs 
             */

            // Always trace the startup request.
            ITracer tracer = TraceStartup(httpContext);

            LogBeginRequest(httpContext);

            // Trace heartbeat periodically
            TraceHeartbeat();

            TryConvertSpecialHeadersToEnvironmentVariable(httpRequest);

            // Skip certain paths
            if (TraceExtensions.ShouldSkipRequest(httpRequest))
            {
                // this is to prevent Kudu being IFrame (typically where host != referer)
                // to optimize where we return X-FRAME-OPTIONS DENY header, only return when 
                // in Azure env, browser non-ajax requests and referer mismatch with host
                // since browser uses referer for other scenarios (such as href, redirect), we may return 
                // this header (benign) in such cases.
                if (Environment.IsAzureEnvironment() && !TraceExtensions.IsAjaxRequest(httpRequest) &&
                    TraceExtensions.MismatchedHostReferer(httpRequest))
                {
                    httpContext.Response.Headers.Add("X-FRAME-OPTIONS", "DENY");
                }

                if (TraceServices.TraceLevel != TraceLevel.Verbose)
                {
                    TraceServices.RemoveRequestTracer(httpContext);

                    // enable just ETW tracer
                    tracer = TraceServices.EnsureEtwTracer(httpContext);
                }
            }

            tracer = tracer ?? TraceServices.CreateRequestTracer(httpContext);

            if (tracer == null || tracer.TraceLevel <= TraceLevel.Off)
            {
                return;
            }

            var attribs = GetTraceAttributes(httpContext);

            AddTraceLevel(httpContext, attribs);

            foreach (string key in httpContext.Request.Headers.Keys)
            {
                if (key != null)
                {
                    if (!key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) &&
                        !key.Equals("X-MS-CLIENT-PRINCIPAL-NAME", StringComparison.OrdinalIgnoreCase))
                    {
                        attribs[key] = httpContext.Request.Headers[key];
                    }
                    else
                    {
                        // for sensitive header, we only trace first 3 characters following by "..."
                        var value = httpContext.Request.Headers[key].ToString();
                        attribs[key] = string.IsNullOrEmpty(value)
                            ? value
                            : (value.Substring(0, Math.Min(3, value.Length)) + "...");
                    }
                }
            }

            httpContext.Items[_stepKey] = tracer.Step(XmlTracer.IncomingRequestTrace, attribs);
            
            AddTrackingHeader(httpContext);
        }

        private static void EndRequest(HttpContext httpContext)
        {
            var tracer = TraceServices.GetRequestTracer(httpContext);
            
            LogEndRequest(httpContext);

            if (tracer == null || tracer.TraceLevel <= TraceLevel.Off)
            {
                return;
            }

            var attribs = new Dictionary<string, string>
            {
                {"type", "response"},
                {"statusCode", httpContext.Response.StatusCode.ToString()},
                {"statusText", GetStatusDescription(httpContext.Response.StatusCode)}
            };

            if (httpContext.Response.StatusCode >= 400)
            {
                attribs[TraceExtensions.TraceLevelKey] = ((int) TraceLevel.Error).ToString();
            }
            else
            {
                AddTraceLevel(httpContext, attribs);
            }

            tracer.Trace(XmlTracer.OutgoingResponseTrace, attribs);

            var requestStep = (IDisposable) httpContext.Items[_stepKey];

            if (requestStep != null)
            {
                requestStep.Dispose();
            }
        }

        // HACK quick hack to replace response.StatusDescription.
        private static string GetStatusDescription(int statusCode)
        {
            return ((HttpStatusCode) statusCode).ToString();
        }

        private static void LogEndRequest(HttpContext httpContext)
        {
            OperationManager.SafeExecute(() =>
            {
                var request = httpContext.Request;
                var response = httpContext.Response;
                var requestId = (string) httpContext.Items[Constants.RequestIdHeader];
                var requestTime = (DateTime) httpContext.Items[Constants.RequestDateTimeUtc];
                var latencyInMilliseconds = (long) (DateTime.UtcNow - requestTime).TotalMilliseconds;
                KuduEventGenerator.Log().ApiEvent(
                    ServerConfiguration.GetApplicationName(),
                    "OnEndRequest",
                    GetRawUrl(request),
                    request.Method,
                    requestId,
                    response.StatusCode,
                    latencyInMilliseconds,
                    request.GetUserAgent());
            });
        }

        private static void AddTraceLevel(HttpContext httpContext, Dictionary<string, string> attribs)
        {
            var rawUrl = GetRawUrl(httpContext.Request);

            if (!rawUrl.StartsWith("/logstream", StringComparison.OrdinalIgnoreCase) &&
                !rawUrl.StartsWith("/deployments", StringComparison.OrdinalIgnoreCase))
            {
                attribs[TraceExtensions.TraceLevelKey] = ((int) TraceLevel.Info).ToString();
            }
        }

        private static string GetRawUrl(HttpRequest request)
        {
            var uri = new Uri(request.GetDisplayUrl());
            return uri.PathAndQuery;
        }

        private static string GetHostUrl(HttpRequest request)
        {
            var uri = new Uri(request.GetDisplayUrl());
            return uri.Host;
        }

        public static TimeSpan LastRequestTime => DateTime.UtcNow - _lastRequestDateTime;
        public static TimeSpan UpTime => DateTime.UtcNow - _startDateTime;

        private static Dictionary<string, string> GetTraceAttributes(HttpContext httpContext)
        {
            var attribs = new Dictionary<string, string>
            {
                {"url", GetRawUrl(httpContext.Request)},
                {"method", httpContext.Request.Method},
                {"type", "request"}
            };

            // Add an attribute containing the process, AppDomain and Thread ids to help debugging
            attribs.Add("pid", String.Join(",",
                Process.GetCurrentProcess().Id,
                AppDomain.CurrentDomain.Id.ToString(),
                Thread.CurrentThread.ManagedThreadId));

            return attribs;
        }

        private static ITracer TraceStartup(HttpContext httpContext)
        {
            ITracer tracer = null;

            // 0 means this is the very first request starting up Kudu
            if (0 == Interlocked.Exchange(ref _traceStartup, 1))
            {
                tracer = TraceServices.CreateRequestTracer(httpContext);

                if (tracer != null && tracer.TraceLevel > TraceLevel.Off)
                {
                    var attribs = GetTraceAttributes(httpContext);

                    // force always trace
                    attribs[TraceExtensions.AlwaysTrace] = "1";

                    // Dump environment variables
                    foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
                    {
                        var key = (string) entry.Key;
                        if (key.StartsWith("SCM", StringComparison.OrdinalIgnoreCase))
                        {
                            attribs[key] = (string) entry.Value;
                        }
                    }

                    tracer.Trace(XmlTracer.StartupRequestTrace, attribs);
                }

                OperationManager.SafeExecute(() =>
                {
                    var requestId = (string) httpContext.Items[Constants.RequestIdHeader];
                    var assembly = Assembly.GetExecutingAssembly();
                    var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                    KuduEventGenerator.Log().GenericEvent(
                        ServerConfiguration.GetApplicationName(),
                        string.Format("StartupRequest pid:{0}, domain:{1}", Process.GetCurrentProcess().Id,
                            AppDomain.CurrentDomain.Id),
                        requestId,
                        System.Environment.GetEnvironmentVariable(SettingsKeys.ScmType),
                        System.Environment.GetEnvironmentVariable(SettingsKeys.WebSiteSku),
                        fvi.FileVersion);
                });
            }

            return tracer;
        }

        private static void TraceHeartbeat()
        {
            var now = DateTime.UtcNow;
            if (_nextHeartbeatDateTime >= now) return;
            _nextHeartbeatDateTime = now.AddHours(1);

            OperationManager.SafeExecute(() =>
            {
                KuduEventGenerator.Log().GenericEvent(
                    ServerConfiguration.GetApplicationName(),
                    string.Format("Heartbeat pid:{0}, domain:{1}", Process.GetCurrentProcess().Id,
                        AppDomain.CurrentDomain.Id),
                    string.Empty,
                    System.Environment.GetEnvironmentVariable(SettingsKeys.ScmType),
                    System.Environment.GetEnvironmentVariable(SettingsKeys.WebSiteSku),
                    KuduVersion.Value);
            });
        }

        private static void LogBeginRequest(HttpContext httpContext)
        {
            OperationManager.SafeExecute(() =>
            {
                var request = httpContext.Request;
                var requestId = request.GetRequestId() ?? Guid.NewGuid().ToString();
                httpContext.Items[Constants.RequestIdHeader] = requestId;
                httpContext.Items[Constants.RequestDateTimeUtc] = DateTime.UtcNow;
                KuduEventGenerator.Log().ApiEvent(
                    ServerConfiguration.GetApplicationName(),
                    "OnBeginRequest",
                    GetRawUrl(request),
                    request.Method,
                    requestId,
                    0,
                    0,
                    request.GetUserAgent());
            });
        }

        private static void TryConvertSpecialHeadersToEnvironmentVariable(HttpRequest request)
        {
            try
            {
                // RDBug 6738223 : AlwaysOn request again SCM has wrong Host Name to main site
                // Ignore Always on request for now till bug is fixed
                if (!string.Equals("AlwaysOn", request.Headers["User-Agent"].ToString(),
                    StringComparison.OrdinalIgnoreCase))
                {
                    System.Environment.SetEnvironmentVariable(Constants.HttpHost, GetHostUrl(request));
                }
            }
            catch
            {
                // this is temporary hack for host name invalid due to ~ (http://~1hostname/)
                // we don't know how to repro it yet.
            }
        }
    }

    public static class TraceMiddlewareExtension
    {
        public static IApplicationBuilder UseTraceMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<TraceMiddleware>();
        }
    }
}
