using System;
using System.Diagnostics;
using Kudu.Contracts.Tracing;
using Kudu.Core.Tracing;
using Microsoft.AspNetCore.Http;

namespace Kudu.Services.Web.Tracing
{
    public static class TraceServices
    {
        private static readonly object TraceKey = new object();
        private static readonly object TraceFileKey = new object();

        private static Func<ITracer> _traceFactory;

        // CORE TODO The CurrentRequestTraceFile - Done and HttpMethod properties
        // were replaced with methods that require the caller to pass the context, as HttpContext.Current
        // no longer exists and is a bad practice anyway. CurrentRequestTracer was removed, as
        // GetRequestTracer(HttpContext) already exists.

        internal static string GetRequestTraceFile(HttpContext context)
        {
            return context?.Items[TraceFileKey] as string;
        }

        internal static string GetHttpMethod(HttpContext context)
        {
            if (context == null)
            {
                return null;
            }

            return context.Request.Method;
        }

        internal static TraceLevel TraceLevel { get; set; }

        public static void SetTraceFactory(Func<ITracer> traceFactory)
        {
            _traceFactory = traceFactory;
        }

        internal static ITracer GetRequestTracer(HttpContext httpContext)
        {
            if (httpContext == null) return null;
            return httpContext.Items[TraceKey] as ITracer;
        }

        internal static void RemoveRequestTracer(HttpContext httpContext)
        {
            httpContext.Items.Remove(TraceKey);
            httpContext.Items.Remove(TraceFileKey);
        }

        internal static ITracer EnsureEtwTracer(HttpContext httpContext)
        {
            var etwTracer = new ETWTracer((string) httpContext.Items[Constants.RequestIdHeader],
                httpContext.Request.Method);

            httpContext.Items[TraceKey] = etwTracer;

            return etwTracer;
        }

        internal static ITracer CreateRequestTracer(HttpContext httpContext)
        {
            var tracer = (ITracer) httpContext.Items[TraceKey];
            if (tracer == null)
            {
                httpContext.Items[TraceFileKey] = String.Format(Constants.TraceFileFormat, Environment.MachineName,
                    Guid.NewGuid().ToString("D"));

                tracer = _traceFactory();
                httpContext.Items[TraceKey] = tracer;
            }

            return tracer;
        }
    }
}