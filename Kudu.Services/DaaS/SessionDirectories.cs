using System;
using System.IO;

namespace Kudu.Services.Performance
{
    internal class SessionDirectories
    {
        private static string SessionsDir { get; }  = Environment.ExpandEnvironmentVariables(@"%HOME%\Data\DaaS\Sessions");
        public static string CompletedSessionsDir { get; } = Path.Combine(SessionsDir, "Complete");
        public static string ActiveSessionsDir { get; } = Path.Combine(SessionsDir, "Active");
    }
}