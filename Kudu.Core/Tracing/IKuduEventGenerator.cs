using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;

namespace Kudu.Core.Tracing
{
    public interface IKuduEventGenerator
    {
        void ProjectDeployed(string siteName, string projectType, string result, string error, long deploymentDurationInMilliseconds, string siteMode, string scmType, string vsProjectId);

        void WebJobStarted(string siteName, string jobName, string scriptExtension, string jobType, string siteMode, string error, string trigger);

        void KuduException(string siteName, string method, string path, string result, string Message, string exception);

        void DeprecatedApiUsed(string siteName, string route, string userAgent, string method, string path);

        void KuduSiteExtensionEvent(string siteName, string method, string path, string result, string deploymentDurationInMilliseconds, string Message);

        void WebJobEvent(string siteName, string jobName, string Message, string jobType, string error);

        void GenericEvent(string siteName, string Message, string requestId, string scmType, string siteMode, string buildVersion);

        void ApiEvent(string siteName, string Message, string address, string verb, string requestId, int statusCode, long latencyInMilliseconds, string userAgent);

        void LogMessage(EventLevel logLevel, string siteName, string message, string exception);

        void DaasSessionMessage(string siteName, string message, string sessionId);

        void DaasSessionException(string siteName, string message, string sessionId, string exception);
    }
}
