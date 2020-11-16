using Renci.SshNet;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Core.Commands
{
    public static class SSHCommandExecutor
    {

        public static async Task ExecuteAsync(
            IProgress<ScriptOutputLine> progress,
            CancellationToken cancellationToken)
        {
            int sshPort = 2222;

            var ipAddress = System.IO.File.ReadAllText("/appsvctmp/ipaddr_" + System.Environment.GetEnvironmentVariable("WEBSITE_ROLE_INSTANCE_ID"));
            if (ipAddress != null && ipAddress.Contains(':'))
            {
                   string[] ipAddrPortStr = ipAddress.Split(":");
                   ipAddress = ipAddrPortStr[0];
                sshPort = Int32.Parse(ipAddrPortStr[1]);
            }

            var taskId = DateTime.Now.ToString("yyyyMMdd.HHmm", CultureInfo.InvariantCulture);
            SshClient sshclient = new SshClient(ipAddress, sshPort, "root", "Docker!");
            sshclient.Connect();
            using (var sshCommand = sshclient.CreateCommand($"sh /diagnostics/take-dump.sh {taskId}"))
            {
                var asyncResult = sshCommand.BeginExecute();
                var stdoutStreamReader = new StreamReader(sshCommand.OutputStream);
                var stderrStreamReader = new StreamReader(sshCommand.ExtendedOutputStream);

                while (!asyncResult.IsCompleted)
                {
                    await CheckOutputAndReportProgress(
                        sshCommand,
                        stdoutStreamReader,
                        stderrStreamReader,
                        progress,
                        cancellationToken);
                }

                sshCommand.EndExecute(asyncResult);

                await CheckOutputAndReportProgress(
                    sshCommand,
                    stdoutStreamReader,
                    stderrStreamReader,
                    progress,
                    cancellationToken);
            }
            sshclient.Disconnect();
        }

        private static async Task CheckOutputAndReportProgress(
            SshCommand sshCommand,
            TextReader stdoutStreamReader,
            TextReader stderrStreamReader,
            IProgress<ScriptOutputLine> progress,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                sshCommand.CancelAsync();
            }
            cancellationToken.ThrowIfCancellationRequested();

            await CheckStdoutAndReportProgressAsync(stdoutStreamReader, progress);
            await CheckStderrAndReportProgressAsync(stderrStreamReader, progress);
        }

        private static async Task CheckStdoutAndReportProgressAsync(
            TextReader stdoutStreamReader,
            IProgress<ScriptOutputLine> stdoutProgress)
        {
            var stdoutLine = await stdoutStreamReader.ReadToEndAsync();

            if (!string.IsNullOrEmpty(stdoutLine))
            {
                stdoutProgress.Report(new ScriptOutputLine(
                    line: stdoutLine,
                    isErrorLine: false));
            }
        }

        private static async Task CheckStderrAndReportProgressAsync(
            TextReader stderrStreamReader,
            IProgress<ScriptOutputLine> stderrProgress)
        {
            var stderrLine = await stderrStreamReader.ReadToEndAsync();

            if (!string.IsNullOrEmpty(stderrLine))
            {
                stderrProgress.Report(new ScriptOutputLine(
                    line: stderrLine,
                    isErrorLine: true));
            }
        }
    }

    public class ScriptOutputLine
    {
        public ScriptOutputLine(string line, bool isErrorLine)
        {
            Line = line;
            IsErrorLine = isErrorLine;
        }

        public string Line { get; private set; }

        public bool IsErrorLine { get; private set; }

    }
}
