using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Kudu.Services.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Services.Web.Tracing
{
    public class TraceMiddleware
    {
        private static readonly object _stepKey = new object();
        private static int _traceStartup;

        private readonly RequestDelegate _next;

        public TraceMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            // CORE TODO Not sure if EndRequest logic makes more sense as a separate middleware
            // we explicitly put at the end of the pipe.
            BeginRequest(context);

            await _next.Invoke(context);

            EndRequest(context);
        }

        private void BeginRequest(HttpContext httpContext)
        {
            var httpRequest = httpContext.Request;

            /* CORE TODO missing functionality:
             * UpTime and LastRequestTime
             * LogBeginRequest (bypass ITracer entirely and log straight to ETW)
             * Disallow GET requests from CSM extensions bridge
             * TryConvertSpecialHeadersToEnvironmentVariable
             * Razor dummy extension for vfs
             * 
             */

            // Always trace the startup request.
            ITracer tracer = TraceStartup(httpContext);

            // Skip certain paths
            if (TraceExtensions.ShouldSkipRequest(httpRequest))
            {
                // this is to prevent Kudu being IFrame (typically where host != referer)
                // to optimize where we return X-FRAME-OPTIONS DENY header, only return when 
                // in Azure env, browser non-ajax requests and referer mismatch with host
                // since browser uses referer for other scenarios (such as href, redirect), we may return 
                // this header (benign) in such cases.
                if (Kudu.Core.Environment.IsAzureEnvironment() && !TraceExtensions.IsAjaxRequest(httpRequest) && TraceExtensions.MismatchedHostReferer(httpRequest))
                {
                    httpContext.Response.Headers.Add("X-FRAME-OPTIONS", "DENY");
                }

                if (TraceServices.TraceLevel != TraceLevel.Verbose)
                {
                    TraceServices.RemoveRequestTracer(httpContext);

                    // enable just ETW tracer
                    tracer = TraceServices.EnsureETWTracer(httpContext);
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
                if (!key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals("X-MS-CLIENT-PRINCIPAL-NAME", StringComparison.OrdinalIgnoreCase) &&
                    !key.Equals(Constants.SiteRestrictedJWT, StringComparison.OrdinalIgnoreCase))
                {
                    attribs[key] = httpContext.Request.Headers[key];
                }
                else
                {
                    // for sensitive header, we only trace first 3 characters following by "..."
                    var value = httpContext.Request.Headers[key].ToString();
                    attribs[key] = string.IsNullOrEmpty(value) ? value : (value.Substring(0, Math.Min(3, value.Length)) + "...");
                }
            }

            httpContext.Items[_stepKey] = tracer.Step(XmlTracer.IncomingRequestTrace, attribs);
        }

        private void EndRequest(HttpContext httpContext)
        {
            var tracer = TraceServices.GetRequestTracer(httpContext);

            LogEndRequest(httpContext);

            if (tracer == null || tracer.TraceLevel <= TraceLevel.Off)
            {
                return;
            }

            var attribs = new Dictionary<string, string>
                {
                    { "type", "response" },
                    { "statusCode", httpContext.Response.StatusCode.ToString() },
                    { "statusText", GetStatusDescription(httpContext.Response.StatusCode) }
                };

            if (httpContext.Response.StatusCode >= 400)
            {
                attribs[TraceExtensions.TraceLevelKey] = ((int)TraceLevel.Error).ToString();
            }
            else
            {
                AddTraceLevel(httpContext, attribs);
            }

            tracer.Trace(XmlTracer.OutgoingResponseTrace, attribs);

            var requestStep = (IDisposable)httpContext.Items[_stepKey];

            if (requestStep != null)
            {
                requestStep.Dispose();
            }
        }

        // HACK quick hack to replace response.StatusDescription.
        private static string GetStatusDescription(int statusCode)
        {
            return ((System.Net.HttpStatusCode)statusCode).ToString();
        }

        private static void LogEndRequest(HttpContext httpContext)
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
                attribs[TraceExtensions.TraceLevelKey] = ((int)TraceLevel.Info).ToString();
            }
        }

        // CORE TODO HttpRequest.RawUrl no longer exists in ASP.NET Core. This reproduces the
        // functionality as best as I can tell, may need some checking.
        private static string GetRawUrl(HttpRequest request)
        {
            var uri = new Uri(request.GetDisplayUrl());
            return uri.PathAndQuery;
        }

        private static Dictionary<string, string> GetTraceAttributes(HttpContext httpContext)
        {
            var attribs = new Dictionary<string, string>
                {
                    { "url", GetRawUrl(httpContext.Request) },
                    { "method", httpContext.Request.Method },
                    { "type", "request" }
                };

            // Add an attribute containing the process, AppDomain and Thread ids to help debugging
            attribs.Add("pid", String.Join(",",
                Process.GetCurrentProcess().Id,
                AppDomain.CurrentDomain.Id.ToString(),
                System.Threading.Thread.CurrentThread.ManagedThreadId));

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
                    foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
                    {
                        var key = (string)entry.Key;
                        if (key.StartsWith("SCM", StringComparison.OrdinalIgnoreCase))
                        {
                            attribs[key] = (string)entry.Value;
                        }
                    }

                    tracer.Trace(XmlTracer.StartupRequestTrace, attribs);
                }

                OperationManager.SafeExecute(() =>
                {
                    var requestId = (string)httpContext.Items[Constants.RequestIdHeader];
                    var assembly = Assembly.GetExecutingAssembly();
                    var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                    KuduEventSource.Log.GenericEvent(
                        ServerConfiguration.GetApplicationName(),
                        string.Format("StartupRequest pid:{0}, domain:{1}", Process.GetCurrentProcess().Id, AppDomain.CurrentDomain.Id),
                        requestId,
                        Environment.GetEnvironmentVariable(SettingsKeys.ScmType),
                        Environment.GetEnvironmentVariable(SettingsKeys.WebSiteSku),
                        fvi.FileVersion);
                });
            }

            return tracer;
        }
    }

    public static class MyMiddlewareExtensions
    {
        public static IApplicationBuilder UseTraceMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<TraceMiddleware>();
        }
    }
}