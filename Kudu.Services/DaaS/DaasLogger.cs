using System;
using Kudu.Core.Helpers;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;

namespace Kudu.Services.DaaS
{
    internal static class DaasLogger
    {
        internal static void LogSessionMessage(string message, string sessionId)
        {
            KuduEventGenerator.Log().DaasSessionMessage(ServerConfiguration.GetApplicationName(),
                 message: message,
                 sessionId: sessionId);

            if (OSDetector.IsOnWindows())
            {
                Console.WriteLine($"[{sessionId}] {message}");
            }
        }

        internal static void LogSessionError(string message, string sessionId, Exception ex)
        {
            KuduEventGenerator.Log().DaasSessionException(ServerConfiguration.GetApplicationName(),
                message: message,
                sessionId: sessionId,
                exception: ex.ToString()); ;

            if (OSDetector.IsOnWindows())
            {
                Console.WriteLine($"[{sessionId}] {message} {ex}");
            }
        }

        internal static void LogSessionError(string message, string sessionId, string error)
        {
            KuduEventGenerator.Log().DaasSessionException(ServerConfiguration.GetApplicationName(),
                message: message,
                sessionId: sessionId,
                exception: error); ;

            if (OSDetector.IsOnWindows())
            {
                Console.WriteLine($"[{sessionId}] {message} {error}");
            }
        }
    }
}
