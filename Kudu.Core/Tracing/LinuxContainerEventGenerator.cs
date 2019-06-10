using System;
using System.IO;
using Kudu.Core.Settings;

namespace Kudu.Core.Tracing
{
    class LinuxContainerEventGenerator : IKuduEventGenerator
    {
        private readonly Action<string> _writeEvent;
        private readonly bool _consoleEnabled = true;

        public LinuxContainerEventGenerator()
        {
            _writeEvent = ConsoleWriter;
        }

        public void ProjectDeployed(string siteName, string projectType, string result, string error, long deploymentDurationInMilliseconds, string siteMode, string scmType, string vsProjectId)
        {
            KuduEvent kuduEvent = new KuduEvent
            {
                siteName = siteName,
                projectType = projectType,
                result = result,
                error = error,
                deploymentDurationInMilliseconds = deploymentDurationInMilliseconds.ToString(),
                siteMode = siteMode,
                scmType = scmType,
                vsProjectId = vsProjectId
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
            KuduEvent kuduEvent = new KuduEvent
            {
                siteName = siteName,
                method = method,
                path = path,
                result = result,
                deploymentDurationInMilliseconds = deploymentDurationInMilliseconds,
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

        public void LogKuduTraceEvent(KuduEvent kuduEvent)
        {
            _writeEvent($"{Constants.LinuxLogEventStreamName} {kuduEvent.ToString()},{Environment.StampName},{Environment.TenantId},{Environment.ContainerName}");
        }

        private void ConsoleWriter(string evt)
        {
            if (_consoleEnabled)
            {
                Console.WriteLine(evt);
            }
        }
    }
}
