using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Infrastructure;

namespace Kudu.Core.Helpers
{
    public static class PermissionHelper
    {
        public static void Chmod(string permission, string filePath, IEnvironment environment, IDeploymentSettingsManager deploymentSettingManager, ILogger logger)
        {
            var folder = Path.GetDirectoryName(filePath);
            var exeFactory = new ExternalCommandFactory(environment, deploymentSettingManager, null);
            Executable exe = exeFactory.BuildCommandExecutable("/bin/chmod", folder, deploymentSettingManager.GetCommandIdleTimeout(), logger);
            exe.Execute("{0} \"{1}\"", permission, filePath);
        }

        public static void ChmodRecursive(string permission, string directoryPath, ITracer tracer, TimeSpan timeout)
        {
            string cmd = String.Format("timeout {0}s chmod {1} -R {2}",timeout.TotalSeconds, permission, directoryPath);
            var escapedArgs = cmd.Replace("\"", "\\\"");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{escapedArgs}\""
                }
            };
            process.Start();
            process.WaitForExit();

            if(process.ExitCode != 0)
            {
                throw new Exception(string.Format("Error in changing file permissions : {0}",process.ExitCode));
            }
        }
    }
}
