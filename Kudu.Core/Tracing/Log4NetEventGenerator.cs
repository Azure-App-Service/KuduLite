using System;
using System.Diagnostics.Tracing;
using System.IO;
using Kudu.Core.Settings;
using NuGet.Protocol.Core.Types;

namespace Kudu.Core.Tracing
{
    class Log4NetEventGenerator : IKuduEventGenerator
    {
        private readonly Action<string> _writeEvent;

        public static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public Log4NetEventGenerator()
        {
            _writeEvent = Log4NetWriter;
        }

        public void ProjectDeployed(string siteName, string projectType, string result, string error, long deploymentDurationInMilliseconds, string siteMode, string scmType, string vsProjectId)
        {
            KuduEvent kuduEvent = new KuduEvent
            {
                siteName = siteName,
                projectType = projectType,
                result = result,
                error = error,
                deploymentDurationInMilliseconds = deploymentDurationInMilliseconds,
                siteMode = siteMode,
                scmType = scmType,
                vsProjectId = vsProjectId
            };

            LogKuduTraceEvent(kuduEvent);
        }

        public void DeploymentCompleted(string siteName, string kind, string requestId, string status, string details)
        {
            KuduEvent kuduEvent = new KuduEvent
            {
                siteName = siteName,
                deploymentDetails = details,
                deploymentStatus = status,
                requestId = requestId,
            };

            LogKuduTraceEvent(kuduEvent);
        }

        public void WebJobStarted(string siteName, string jobName, string scriptExtension, string jobType, string siteMode, string error, string trigger)
        {
            KuduEvent kuduEvent = new KuduEvent
            {
                siteName = siteName,
                jobName = jobName,
                scriptExtension = scriptExtension,
                jobType = jobType,
                siteMode = siteMode,
                error = error,
                trigger = trigger
            };

            LogKuduTraceEvent(kuduEvent);
        }

        public void KuduException(string siteName, string method, string path, string result, string Message, string exception)
        {
            KuduEvent kuduEvent = new KuduEvent
            {
                level = (int)EventLevel.Warning,
                siteName = siteName,
                method = method,
                path = path,
                result = result,
                Message = Message,
                exception = exception
            };

            LogKuduTraceEvent(kuduEvent);
        }

        public void DeprecatedApiUsed(string siteName, string route, string userAgent, string method, string path)
        {
            KuduEvent kuduEvent = new KuduEvent
            {
                level = (int)EventLevel.Warning,
                siteName = siteName,
                route = route,
                userAgent = userAgent,
                method = method,
                path = path
            };

            LogKuduTraceEvent(kuduEvent);
        }

        public void KuduSiteExtensionEvent(string siteName, string method, string path, string result, string deploymentDurationInMilliseconds, string Message)
        {
            long duration = 0;
            long.TryParse(deploymentDurationInMilliseconds, out duration);
            KuduEvent kuduEvent = new KuduEvent
            {
                siteName = siteName,
                method = method,
                path = path,
                result = result,
                deploymentDurationInMilliseconds = duration,
                Message = Message
            };

            LogKuduTraceEvent(kuduEvent);
        }

        public void WebJobEvent(string siteName, string jobName, string Message, string jobType, string error)
        {
            KuduEvent kuduEvent = new KuduEvent
            {
                siteName = siteName,
                jobName = jobName,
                Message = Message,
                jobType = jobType,
                error = error
            };

            LogKuduTraceEvent(kuduEvent);
        }

        public void GenericEvent(string siteName, string Message, string requestId, string scmType, string siteMode, string buildVersion)
        {
            KuduEvent kuduEvent = new KuduEvent
            {
                siteName = siteName,
                Message = Message,
                requestId = requestId,
                scmType = scmType,
                siteMode = siteMode,
                buildVersion = buildVersion
            };

            LogKuduTraceEvent(kuduEvent);
        }

        public void ApiEvent(string siteName, string Message, string address, string verb, string requestId, int statusCode, long latencyInMilliseconds, string userAgent)
        {
            KuduEvent kuduEvent = new KuduEvent
            {
                siteName = siteName,
                Message = Message,
                address = address,
                verb = verb,
                requestId = requestId,
                statusCode = statusCode,
                latencyInMilliseconds = latencyInMilliseconds,
                userAgent = userAgent
            };

            LogKuduTraceEvent(kuduEvent);
        }

        public void LogMessage(EventLevel logLevel, string siteName, string message, string exception)
        {
            // Only used in Linux consumption
            return;
        }


        public void LogKuduTraceEvent(KuduEvent kuduEvent)
        {
            _writeEvent($"{Constants.LinuxLogEventStreamName} {kuduEvent.ToString()},{Environment.StampName},{Environment.TenantId},{Environment.ContainerName}");
        }

        private void Log4NetWriter(string evt)
        {
                log.Debug(evt);
        }
    }
}
