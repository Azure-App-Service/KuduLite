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
            KuduEventGenerator.Log().GenericEvent(ServerConfiguration.GetApplicationName(),
                 $"[{sessionId}] {message}",
                 string.Empty,
                 string.Empty,
                 string.Empty,
                 string.Empty);

            if (OSDetector.IsOnWindows())
            {
                Console.WriteLine($"[{sessionId}] {message}");
            }
        }

        internal static void LogSessionError(string message, string sessionId, Exception ex)
        {
            KuduEventGenerator.Log().GenericEvent(ServerConfiguration.GetApplicationName(),
                 $"[{sessionId}] {message} {ex}",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);

            if (OSDetector.IsOnWindows())
            {
                Console.WriteLine($"[{sessionId}] {message} {ex}");
            }
        }
    }
}
