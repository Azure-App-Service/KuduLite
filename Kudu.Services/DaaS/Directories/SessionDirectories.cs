using System.IO;

namespace Kudu.Services.DaaS
{
    internal class SessionDirectories : DaasDirectory
    {
        private static readonly string sessionsDir = Path.Combine(daasPath, "Sessions");

        public static string CompletedSessionsDir { get; } = Path.Combine(sessionsDir, "Complete");
        public static string ActiveSessionsDir { get; } = Path.Combine(sessionsDir, "Active");
    }
}