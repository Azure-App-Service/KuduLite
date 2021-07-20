using System.IO;

namespace Kudu.Services.DaaS
{
    internal class LogsDirectories : DaasDirectory
    {
        public static string LogsDir { get; } = Path.Combine(daasPath, "Logs");
        public static string LogsDirRelativePath { get; } = Path.Combine(daasRelativePath, "Logs");
    }
}