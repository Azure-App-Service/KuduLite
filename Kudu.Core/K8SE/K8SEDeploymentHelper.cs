using Kudu.Contracts.Tracing;
using Kudu.Core.Deployment;
using Kudu.Core.Functions;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
            return RunBuildCtlCommand(cmd.ToString(), "Retrieving framework info...");
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
            RunBuildCtlCommand(cmd.ToString(), "Updating build version...");
        }

        /// <summary>
        /// Updates the triggers for the function apps
        /// </summary>
        /// <param name="appName">The app name to update</param>
        /// <param name="functionTriggers">The IEnumerable<ScaleTrigger></param>
        /// <param name="buildNumber">Build number to update</param>
        public static void UpdateFunctionAppTriggers(string appName, IEnumerable<ScaleTrigger> functionTriggers, string buildNumber)
        {
            var functionAppPatchJson = GetFunctionAppPatchJson(functionTriggers, buildNumber);
            if (string.IsNullOrEmpty(functionAppPatchJson))
            {
                return;
            }

            var cmd = new StringBuilder();
            BuildCtlArgumentsHelper.AddBuildCtlCommand(cmd, "updatejson");
            BuildCtlArgumentsHelper.AddAppNameArgument(cmd, appName);
            BuildCtlArgumentsHelper.AddFunctionTriggersJsonToPatchValueArgument(cmd, functionAppPatchJson);
            RunBuildCtlCommand(cmd.ToString(), "Updating function app triggers...");
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
            var appName = context.Request.Headers["K8SE_APP_NAME"].ToString();

            if (string.IsNullOrEmpty(appName))
            {
                context.Response.StatusCode = 401;
                // K8SE TODO: move this to resource map
                throw new InvalidOperationException("Couldn't recognize AppName");
            }

            Console.WriteLine("AppName :::::::: " + appName);

            return appName;
        }

        private static string GetFunctionAppPatchJson(IEnumerable<ScaleTrigger> functionTriggers, string buildNumber)
        {
            if (functionTriggers == null || !functionTriggers.Any())
            {
                return null;
            }

            var patchAppJson = new PatchAppJson
            {
                PatchSpec = new PatchSpec
                {
                    TriggerOptions = new TriggerOptions
                    {
                        Triggers = functionTriggers
                    },
                    Code = new CodeSpec
                    {
                        PackageRef = new PackageReference
                        {
                            BuildVersion = buildNumber
                        }
                    }
                }
            };

            var patchJson = JsonConvert.SerializeObject(patchAppJson, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            //var patchJsonString = JsonConvert.ToString(patchJson);
            return patchJson;
        }
    }
}
