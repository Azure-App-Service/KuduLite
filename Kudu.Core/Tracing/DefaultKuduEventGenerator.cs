using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics.Tracing;

namespace Kudu.Core.Tracing
{
    class DefaultKuduEventGenerator : IKuduEventGenerator
    {
        public void ProjectDeployed(string siteName, string projectType, string result, string error, long deploymentDurationInMilliseconds, string siteMode, string scmType, string vsProjectId)
        {
            KuduEventSource.Log.ProjectDeployed(siteName, projectType, result, error, deploymentDurationInMilliseconds, siteMode, scmType, vsProjectId);
        }

        public void WebJobStarted(string siteName, string jobName, string scriptExtension, string jobType, string siteMode, string error, string trigger)
        {
            KuduEventSource.Log.WebJobStarted(siteName, jobName, scriptExtension, jobType, siteMode, error, trigger);
        }

        public void KuduException(string siteName, string method, string path, string result, string Message, string exception)
        {
            KuduEventSource.Log.KuduException(siteName, method, path, result, Message, exception);
        }

        public void DeprecatedApiUsed(string siteName, string route, string userAgent, string method, string path)
        {
            KuduEventSource.Log.DeprecatedApiUsed(siteName, route, userAgent, method, path);
        }
        
        public void KuduSiteExtensionEvent(string siteName, string method, string path, string result, string deploymentDurationInMilliseconds, string Message)
        {
            KuduEventSource.Log.KuduSiteExtensionEvent(siteName, method, path, result, deploymentDurationInMilliseconds, Message);
        }

        public void WebJobEvent(string siteName, string jobName, string Message, string jobType, string error)
        {
            KuduEventSource.Log.WebJobEvent(siteName, jobName, Message, jobType, error);
        }

        public void GenericEvent(string siteName, string Message, string requestId, string scmType, string siteMode, string buildVersion)
        {
            KuduEventSource.Log.GenericEvent(siteName, Message, requestId, scmType, siteMode, buildVersion);
        }

        public void ApiEvent(string siteName, string Message, string address, string verb, string requestId, int statusCode, long latencyInMilliseconds, string userAgent)
        {
            KuduEventSource.Log.ApiEvent(siteName, Message, address, verb, requestId, statusCode, latencyInMilliseconds, userAgent);
        }

        public void LogMessage(EventLevel logLevel, string siteName, string message, string exception)
        {
            // Only used in Linux consumption currently
            return;
        }
    }
}
