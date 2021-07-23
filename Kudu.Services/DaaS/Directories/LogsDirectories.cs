using System.IO;

namespace Kudu.Services.DaaS
{
    internal class LogsDirectories : DaasDirectory
    {
        internal static string LogsDir { get; } = Path.Combine(daasPath, "Logs");
        internal static string LogsDirRelativePath { get; } = Path.Combine(daasRelativePath, "Logs");
    }
}