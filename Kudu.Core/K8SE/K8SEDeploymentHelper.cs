using Kudu.Contracts.Tracing;
using Kudu.Core.Deployment;
using Microsoft.AspNetCore.Http;
using System;
using System.Diagnostics;
using System.Text;

namespace Kudu.Core.K8SE
{
    public static class K8SEDeploymentHelper
    {

        public static ITracer _tracer;
        public static ILogger _logger;

        // K8SE_BUILD_SERVICE not null or empty
        public static bool IsK8SEEnvironment()
        {
            return !String.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(Constants.IsK8SEEnvironment));
        }

        /// <summary>
        /// Calls into buildctl to retrieve BuildVersion of
        /// the K8SE App
        /// </summary>
        /// <param name="appName"></param>
        /// <returns></returns>
        public static string GetLinuxFxVersion(string appName)
        {
            var cmd = new StringBuilder();
            BuildCtlArgumentsHelper.AddBuildCtlCommand(cmd, "get");
            BuildCtlArgumentsHelper.AddAppNameArgument(cmd, appName);
            BuildCtlArgumentsHelper.AddAppPropertyArgument(cmd, "linuxFxVersion");
            return RunBuildCtlCommand(cmd.ToString(), "Running buildctl to retrieve framework info...");
        }

        /// <summary>
        /// Calls into buildctl to update a BuildVersion of
        /// the K8SE App
        /// </summary>
        /// <param name="appName"></param>
        /// <returns></returns>
        public static void UpdateBuildNumber(string appName, string buildNumber)
        {
            var cmd = new StringBuilder();
            BuildCtlArgumentsHelper.AddBuildCtlCommand(cmd, "update");
            BuildCtlArgumentsHelper.AddAppNameArgument(cmd, appName);
            BuildCtlArgumentsHelper.AddAppPropertyArgument(cmd, "buildVersion");
            BuildCtlArgumentsHelper.AddAppPropertyValueArgument(cmd, buildNumber);
            RunBuildCtlCommand(cmd.ToString(), "Running buildctl to retrieve framework info...");
        }

        private static string RunBuildCtlCommand(string args, string msg)
        {
            Console.WriteLine($"{msg} : {args}");
            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{args}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (string.IsNullOrEmpty(error)) 
            { 
                return output; 
            }
            else 
            { 
                throw new Exception(error); 
            }
        }

        public static string GetAppName(HttpContext context)
        {
            var host = context.Request.Headers["Host"].ToString();
            var appName = "";

            if (host.IndexOf(".") <= 0)
            {
                context.Response.StatusCode = 401;
                // K8SE TODO: move this to resource map
                throw new InvalidOperationException("Couldn't recognize AppName");
            }
            else
            {
                appName = host.Substring(0, host.IndexOf("."));
            }
            Console.WriteLine("AppName :::::::: " + appName);

            try
            {
                // block any internal service communication to build server
                // K8SE TODO: Check source IP to be in the internal service range and block them
                Int32.Parse(appName);
                context.Response.StatusCode = 401;
                // K8SE TODO: move this to resource map
                throw new InvalidOperationException("Internal services prohibited to communicate with the build server.");
            }
            catch(Exception)
            {
                // pass
            }

            return appName;
        }
    }
}
