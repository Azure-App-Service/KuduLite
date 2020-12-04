using Kudu.Contracts.Deployment;
using Kudu.Contracts.Tracing;
using Kudu.Core.Deployment;
using Kudu.Core.Functions;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace Kudu.Core.K8SE
{
    public static class K8SEDeploymentHelper
    {

        public static ITracer _tracer;
        public static ILogger _logger;
        private static ObjectCache cache = MemoryCache.Default;
        private static CacheItemPolicy instanceCachePolicy = new CacheItemPolicy
        {
            AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(30.0),

        };

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
        /// Calls into buildctl to get a list of instaces for an app
        /// </summary>
        /// <param name="appName"></param>
        /// <returns></returns>
        public static List<PodInstance> GetInstances(string appName)
        {
            var cachedInstances = cache.Get(appName);
            if (cachedInstances == null)
            {
                var cmd = new StringBuilder();
                BuildCtlArgumentsHelper.AddBuildCtlCommand(cmd, "get");
                BuildCtlArgumentsHelper.AddAppNameArgument(cmd, appName);
                BuildCtlArgumentsHelper.AddAppPropertyArgument(cmd, "podInstances");
                var instList = RunBuildCtlCommand(cmd.ToString(), "Getting app instances...");
                byte[] data = Convert.FromBase64String(instList);
                string json = Encoding.UTF8.GetString(data);
                cachedInstances = JsonConvert.DeserializeObject<List<PodInstance>>(json);
                cache.Add(appName, cachedInstances, instanceCachePolicy);
            }

            return (List<PodInstance>)cachedInstances;
        }

        /// <summary>
        /// Calls into buildctl to update a BuildVersion of
        /// the K8SE App
        /// </summary>
        /// <param name="appName"></param>
        /// <returns></returns>
        public static void UpdateBuildNumber(string appName, BuildMetadata buildMetadata)
        {
            var cmd = new StringBuilder();
            BuildCtlArgumentsHelper.AddBuildCtlCommand(cmd, "update");
            BuildCtlArgumentsHelper.AddAppNameArgument(cmd, appName);
            BuildCtlArgumentsHelper.AddAppPropertyArgument(cmd, "buildMetadata");
            BuildCtlArgumentsHelper.AddAppPropertyValueArgument(cmd, $"\\\"{GetBuildMetadataStr(buildMetadata)}\\\"");
            RunBuildCtlCommand(cmd.ToString(), "Updating build version...");
        }

        /// <summary>
        /// Updates the Image Tag of the K8SE custom container app
        /// </summary>
        /// <param name="appName"></param>
        /// <param name="imageTag">container image tag of the format registry/<image>:<tag></param>
        /// <returns></returns>
        public static void UpdateImageTag(string appName, string imageTag)
        {
            var cmd = new StringBuilder();
            BuildCtlArgumentsHelper.AddBuildCtlCommand(cmd, "update");
            BuildCtlArgumentsHelper.AddAppNameArgument(cmd, appName);
            BuildCtlArgumentsHelper.AddAppPropertyArgument(cmd, "appImage");
            BuildCtlArgumentsHelper.AddAppPropertyValueArgument(cmd, imageTag);
            RunBuildCtlCommand(cmd.ToString(), "Updating image tag...");
        }

        /// <summary>
        /// Updates the triggers for the function apps
        /// </summary>
        /// <param name="appName">The app name to update</param>
        /// <param name="functionTriggers">The IEnumerable<ScaleTrigger></param>
        /// <param name="buildNumber">Build number to update</param>
        public static void UpdateFunctionAppTriggers(string appName, IEnumerable<ScaleTrigger> functionTriggers, BuildMetadata buildMetadata)
        {
            var functionAppPatchJson = GetFunctionAppPatchJson(functionTriggers, buildMetadata);
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
            Console.WriteLine($"buildctl output:\n {output}");
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
            return appName;
        }

        public static string GetAppKind(HttpContext context)
        {
            var appKind = context.Request.Headers["K8SE_APP_KIND"].ToString();
            //K8SE_APP_KIND is only needed for the logic apps, for web apps and function apps, fallback to "kubeapp"
            appKind = string.IsNullOrEmpty(appKind) ? "kubeapp" : appKind;
            if (string.IsNullOrEmpty(appKind))
            {
                context.Response.StatusCode = 401;
                // K8SE TODO: move this to resource map
                throw new InvalidOperationException("Couldn't recognize AppKind");
            }

            return appKind;           
        }

        public static string GetAppNamespace(HttpContext context)
        {
            var appNamepace = context.Request.Headers["K8SE_APP_NAMESPACE"].ToString();
            return appNamepace;
        }

        public static void UpdateContextWithAppSettings(HttpContext context)
        {
            Dictionary<string, string> appSettings = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            var appSettingsPrefix = "appsetting_";
            var appSettingsWithHeader = context.Request.Headers
                .Where(p => p.Key.StartsWith(appSettingsPrefix, StringComparison.OrdinalIgnoreCase));

            foreach (var setting in appSettingsWithHeader)
            {
                var key = setting.Key.Substring(appSettingsPrefix.Length);
                appSettings[key] = setting.Value;
            }
            context.Items.TryAdd("appSettings", appSettings);
        }

        private static string GetFunctionAppPatchJson(IEnumerable<ScaleTrigger> functionTriggers, BuildMetadata buildMetadata)
        {
            if (functionTriggers == null || !functionTriggers.Any())
            {
                return null;
            }

            if (buildMetadata == null )
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
                            BuildMetadata = GetBuildMetadataStr(buildMetadata),
                        }
                    }
                }
            };

            var str= System.Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(JsonConvert.SerializeObject(patchAppJson)));
            Console.WriteLine("Test Str:     " + str);
            return str;
        }

        private static string GetBuildMetadataStr(BuildMetadata buildMetadata)
        {
            return $"{buildMetadata.AppName}|{buildMetadata.BuildVersion}|{System.Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes(JsonConvert.SerializeObject(buildMetadata)))}";
        }
    }
}
