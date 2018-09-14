using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Deployment;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Kudu.Services.Web.Tracing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;

namespace Kudu.Services.Web
{
    public static class Util
    {

        private static Uri GetAbsoluteUri(HttpContext httpContext)
        {
            var request = httpContext.Request;
            UriBuilder uriBuilder = new UriBuilder();
            uriBuilder.Scheme = request.Scheme;
            uriBuilder.Host = request.Host.Host;
            uriBuilder.Path = request.Path.ToString();
            uriBuilder.Query = request.QueryString.ToString();
            return uriBuilder.Uri;
        }

        public static ITracer GetTracer(IServiceProvider serviceProvider)
        {
            var environment = serviceProvider.GetRequiredService<IEnvironment>();
            var level = serviceProvider.GetRequiredService<IDeploymentSettingsManager>().GetTraceLevel();
            var contextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
            var httpContext = contextAccessor.HttpContext;
            var requestTraceFile = TraceServices.GetRequestTraceFile(httpContext);
            if (level <= TraceLevel.Off || requestTraceFile == null) return NullTracer.Instance;
            var textPath = Path.Combine(environment.TracePath, requestTraceFile);
            return new CascadeTracer(new XmlTracer(environment.TracePath, level), new TextTracer(textPath, level), new ETWTracer(environment.RequestId, TraceServices.GetHttpMethod(httpContext)));
        }

        public static string GetRequestTraceFile(IServiceProvider serviceProvider)
        {
            var traceLevel = serviceProvider.GetRequiredService<IDeploymentSettingsManager>().GetTraceLevel();
            // CORE TODO Need TraceServices implementation
            //if (level > TraceLevel.Off)
            //{
            //    return TraceServices.CurrentRequestTraceFile;
            //}

            return null;
        }

        public static ITracer GetTracerWithoutContext(IEnvironment environment, IDeploymentSettingsManager settings)
        {
            // when file system has issue, this can throw (environment.TracePath calls EnsureDirectory).
            // prefer no-op tracer over outage.  
            return OperationManager.SafeExecute(() =>
            {
                var traceLevel = settings.GetTraceLevel();
                return traceLevel > TraceLevel.Off ? new XmlTracer(environment.TracePath, traceLevel) : NullTracer.Instance;
            }) ?? NullTracer.Instance;
        }

        public static ILogger GetLogger(IServiceProvider serviceProvider)
        {
            var environment = serviceProvider.GetRequiredService<IEnvironment>();
            var level = serviceProvider.GetRequiredService<IDeploymentSettingsManager>().GetTraceLevel();
            var contextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
            var httpContext = contextAccessor.HttpContext;
            var requestTraceFile = TraceServices.GetRequestTraceFile(httpContext);
            if (level <= TraceLevel.Off || requestTraceFile == null) return NullLogger.Instance;
            var textPath = Path.Combine(environment.DeploymentTracePath, requestTraceFile);
            return new TextLogger(textPath);
        }

    }
}
