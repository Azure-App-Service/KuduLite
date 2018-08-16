﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Contracts.Settings;
using Kudu.Core.Infrastructure;
using Newtonsoft.Json;

namespace Kudu.Core.Helpers
{
    public static class PostDeploymentHelper
    {
        public const string AutoSwapLockFile = "autoswap.lock";

        private static Lazy<ProductInfoHeaderValue> _userAgent = new Lazy<ProductInfoHeaderValue>(() =>
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            return new ProductInfoHeaderValue("kudu", fvi.FileVersion);
        });

        private static TraceListener _tracer;
        private static Func<HttpClient> _httpClientFactory;

        // for mocking or override behavior
        public static Func<HttpClient> HttpClientFactory
        {
            get { return _httpClientFactory ?? new Func<HttpClient>(() => new HttpClient()); }
            set { _httpClientFactory = value; }
        }

        private static string AutoSwapLockFilePath
        {
            get { return System.Environment.ExpandEnvironmentVariables(@"%HOME%\site\locks\" + AutoSwapLockFile); }
        }

        // HTTP_HOST = site.scm.azurewebsites.net
        private static string HttpHost
        {
            get { return System.Environment.GetEnvironmentVariable(Constants.HttpHost); }
        }

        // WEBSITE_SWAP_SLOTNAME = Production
        private static string WebSiteSwapSlotName
        {
            get { return System.Environment.GetEnvironmentVariable(Constants.WebSiteSwapSlotName); }
        }

        // FUNCTIONS_EXTENSION_VERSION = ~1.0
        private static string FunctionRunTimeVersion
        {
            get { return System.Environment.GetEnvironmentVariable(Constants.FunctionRunTimeVersion); }
        }

        // ROUTING_EXTENSION_VERSION = 1.0
        private static string RoutingRunTimeVersion
        {
            get { return System.Environment.GetEnvironmentVariable(Constants.RoutingRunTimeVersion); }
        }

        // LOGICAPP_URL = [url to PUT logicapp.json to]
        private static string LogicAppUrl
        {
            get { return System.Environment.GetEnvironmentVariable(Constants.LogicAppUrlKey); }
        }

        // %HOME%\site\wwwroot\logicapp.json
        private static string LogicAppJsonFilePath
        {
            get { return System.Environment.ExpandEnvironmentVariables(@"%HOME%\site\wwwroot\" + Constants.LogicAppJson); }
        }

        // WEBSITE_SKU = Dynamic
        private static string WebSiteSku
        {
            get { return System.Environment.GetEnvironmentVariable(Constants.WebSiteSku); }
        }

        // WEBSITE_INSTANCE_ID not null or empty
        public static bool IsAzureEnvironment()
        {
            return !String.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
        }

        /// <summary>
        /// This common codes is to invoke post deployment operations.
        /// It is written to require least dependencies but framework assemblies.
        /// Caller is responsible for synchronization.
        /// </summary>
        public static async Task Run(string requestId, string siteRestrictedJwt, TraceListener tracer)
        {
            RunPostDeploymentScripts(tracer);

            await SyncFunctionsTriggers(requestId, siteRestrictedJwt, tracer);

            await PerformAutoSwap(requestId, siteRestrictedJwt, tracer);
        }

        public static async Task SyncFunctionsTriggers(string requestId, string siteRestrictedJwt, TraceListener tracer)
        {
            _tracer = tracer;

            if (string.IsNullOrEmpty(FunctionRunTimeVersion))
            {
                Trace(TraceEventType.Verbose, "Skip function trigger and logicapp sync because function is not enabled.");
                return;
            }

            if (!string.Equals(Constants.DynamicSku, WebSiteSku, StringComparison.OrdinalIgnoreCase))
            {
                Trace(TraceEventType.Verbose, string.Format("Skip function trigger and logicapp sync because sku ({0}) is not dynamic (consumption plan).", WebSiteSku));
                return;
            }

            VerifyEnvironments();

            var funtionsPath = System.Environment.ExpandEnvironmentVariables(@"%HOME%\site\wwwroot");
            var triggers = Directory
                    .GetDirectories(funtionsPath)
                    .Select(d => Path.Combine(d, Constants.FunctionsConfigFile))
                    .Where(File.Exists)
                    .SelectMany(f => DeserializeFunctionTrigger(f))
                    .ToList();

            if (!string.IsNullOrEmpty(RoutingRunTimeVersion))
            {
                triggers.Add(new Dictionary<string, object> { { "type", "routingTrigger" } });
            }

            var content = JsonConvert.SerializeObject(triggers);
            Exception exception = null;
            try
            {
                await PostAsync("/operations/settriggers", requestId, siteRestrictedJwt, content);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                Trace(TraceEventType.Information,
                      "Syncing {0} function triggers with payload size {1} bytes {2}",
                      triggers.Count,
                      content.Length,
                      exception == null ? "successful." : ("failed with " + exception));
            }

            // this couples with sync function triggers
            await SyncLogicAppJson(requestId, tracer);
        }

        public static async Task SyncLogicAppJson(string requestId, TraceListener tracer)
        {
            _tracer = tracer;

            var logicAppUrl = LogicAppUrl;
            if (string.IsNullOrEmpty(logicAppUrl))
            {
                return;
            }

            var fileInfo = new FileInfo(LogicAppJsonFilePath);
            if (!fileInfo.Exists)
            {
                Trace(TraceEventType.Verbose, "File {0} does not exists", fileInfo.FullName);
                return;
            }

            var displayUrl = logicAppUrl;
            var queryIndex = logicAppUrl.IndexOf('?');
            if (queryIndex > 0)
            {
                // for display/logging, strip out querystring secret
                displayUrl = logicAppUrl.Substring(0, queryIndex);
            }

            var content = File.ReadAllText(fileInfo.FullName);
            var statusCode = default(HttpStatusCode);
            Exception exception = null;
            try
            {
                Trace(TraceEventType.Verbose, "Begin HttpPut {0}, x-ms-client-request-id: {1}", displayUrl, requestId);

                using (var client = HttpClientFactory())
                {
                    client.DefaultRequestHeaders.UserAgent.Add(_userAgent.Value);
                    client.DefaultRequestHeaders.Add(Constants.ClientRequestIdHeader, requestId);

                    var payload = new StringContent(content ?? string.Empty, Encoding.UTF8, "application/json");
                    using (var response = await client.PutAsync(logicAppUrl, payload))
                    {
                        statusCode = response.StatusCode;
                        response.EnsureSuccessStatusCode();
                    }
                }
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                Trace(TraceEventType.Verbose, "End HttpPut, status: {0}", statusCode);

                Trace(TraceEventType.Information,
                      "Syncing logicapp {0} with payload size {1} bytes {2}",
                      displayUrl,
                      content.Length,
                      exception == null ? "successful." : ("failed with " + exception));
            }
        }

        public static bool IsAutoSwapOngoing()
        {
            if (string.IsNullOrEmpty(WebSiteSwapSlotName))
            {
                return false;
            }

            // Auto swap is ongoing if the auto swap lock file exists and is written to less than 2 minutes ago
            var fileInfo = new FileInfo(AutoSwapLockFilePath);
            return fileInfo.Exists && fileInfo.LastWriteTimeUtc.AddMinutes(2) >= DateTime.UtcNow;
        }

        public static bool IsAutoSwapEnabled()
        {
            return !string.IsNullOrEmpty(WebSiteSwapSlotName);
        }

        public static async Task PerformAutoSwap(string requestId, string siteRestrictedJwt, TraceListener tracer)
        {
            _tracer = tracer;

            var slotSwapName = WebSiteSwapSlotName;
            if (string.IsNullOrEmpty(slotSwapName))
            {
                Trace(TraceEventType.Verbose, "AutoSwap is not enabled");
                return;
            }

            VerifyEnvironments();

            var operationId = string.Format("AUTOSWAP{0}", Guid.NewGuid());
            Exception exception = null;
            try
            {
                await PostAsync(string.Format("/operations/autoswap?slot={0}&operationId={1}", slotSwapName, operationId), requestId, siteRestrictedJwt);

                WriteAutoSwapOngoing();
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                Trace(TraceEventType.Information,
                      "Requesting auto swap to '{0}' slot with '{1}' id {2}",
                      slotSwapName,
                      operationId,
                      exception == null ? "successful." : ("failed with " + exception));
            }
        }

        private static void VerifyEnvironments()
        {
            if (string.IsNullOrEmpty(HttpHost))
            {
                throw new InvalidOperationException(String.Format("Missing {0} env!", Constants.HttpHost));
            }
        }

        private static IEnumerable<Dictionary<string, object>> DeserializeFunctionTrigger(string functionJson)
        {
            try
            {
                var functionName = Path.GetFileName(Path.GetDirectoryName(functionJson));
                var json = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(functionJson));

                object value;
                // https://github.com/Azure/azure-webjobs-sdk-script/blob/a9bafba78a3a8092bfd61a8c7093200dae867efb/src/WebJobs.Script/Host/ScriptHost.cs#L1476-L1498
                if (json.TryGetValue("disabled", out value))
                {
                    string stringValue = value.ToString();
                    bool disabled;
                    // if "disabled" is not a boolean, we try to expend it as an environment variable
                    if (!Boolean.TryParse(stringValue, out disabled))
                    {
                        string expandValue = System.Environment.GetEnvironmentVariable(stringValue);
                        // "1"/"true" -> true, else false
                        disabled = string.Equals(expandValue, "1", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(expandValue, "true", StringComparison.OrdinalIgnoreCase);
                    }

                    if (disabled)
                    {
                        Trace(TraceEventType.Verbose, "Function {0} is disabled", functionName);
                        return Enumerable.Empty<Dictionary<string, object>>();
                    }
                }

                var excluded = json.TryGetValue("excluded", out value) && (bool)value;
                if (excluded)
                {
                    Trace(TraceEventType.Verbose, "Function {0} is excluded", functionName);
                    return Enumerable.Empty<Dictionary<string, object>>();
                }

                var triggers = new List<Dictionary<string, object>>();
                foreach (Dictionary<string, object> binding in (object[])json["bindings"])
                {
                    var type = (string)binding["type"];
                    if (type.EndsWith("Trigger", StringComparison.OrdinalIgnoreCase))
                    {
                        binding.Add("functionName", functionName);
                        Trace(TraceEventType.Verbose, "Syncing {0} of {1}", type, functionName);
                        triggers.Add(binding);
                    }
                    else
                    {
                        Trace(TraceEventType.Verbose, "Skipping {0} of {1}", type, functionName);
                    }
                }

                return triggers;
            }
            catch (Exception ex)
            {
                Trace(TraceEventType.Warning, "{0} is invalid. {1}", functionJson, ex);
            }

            return Enumerable.Empty<Dictionary<string, object>>();
        }

        private static void WriteAutoSwapOngoing()
        {
            var autoSwapLockFilePath = AutoSwapLockFilePath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(autoSwapLockFilePath));
                File.WriteAllText(autoSwapLockFilePath, string.Empty);
            }
            catch (Exception ex)
            {
                // best effort
                Trace(TraceEventType.Warning, "Fail to write {0}.  {1}", autoSwapLockFilePath, ex);
            }
        }

        private static async Task PostAsync(string path, string requestId, string siteRestrictedJwt, string content = null)
        {
            var host = HttpHost;
            var statusCode = default(HttpStatusCode);
            Trace(TraceEventType.Verbose, "Begin HttpPost https://{0}{1}, x-ms-request-id: {2}", host, path, requestId);
            try
            {
                using (var client = HttpClientFactory())
                {
                    client.BaseAddress = new Uri(string.Format("https://{0}", host));
                    client.DefaultRequestHeaders.UserAgent.Add(_userAgent.Value);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", siteRestrictedJwt);
                    client.DefaultRequestHeaders.Add(Constants.RequestIdHeader, requestId);

                    var payload = new StringContent(content ?? string.Empty, Encoding.UTF8, "application/json");
                    using (var response = await client.PostAsync(path, payload))
                    {
                        statusCode = response.StatusCode;
                        response.EnsureSuccessStatusCode();
                    }
                }
            }
            finally
            {
                Trace(TraceEventType.Verbose, "End HttpPost, status: {0}", statusCode);
            }
        }

        public static void RunPostDeploymentScripts(TraceListener tracer)
        {
            _tracer = tracer;

            foreach (var file in GetPostBuildActionScripts())
            {
                ExecuteScript(file);
            }
        }

        /// <summary>
        /// As long as the task was not completed, we will keep updating the marker file.
        /// The routine completes when either task completes or timeout.
        /// If task is completed, we will remove the marker.
        /// If timeout, we will leave the stale marker.
        /// </summary>
        public static async Task TrackPendingOperation(Task task, TimeSpan timeout)
        {
            const int DefaultTimeoutMinutes = 30;
            const int DefaultUpdateMarkerIntervalMS = 10000;
            const string MarkerFilePath = @"%TEMP%\SCMPendingOperation.txt";

            // only applicable to azure env
            if (!IsAzureEnvironment())
            {
                return;
            }

            if (timeout <= TimeSpan.Zero || timeout >= TimeSpan.FromMinutes(DefaultTimeoutMinutes))
            {
                // track at most N mins by default
                timeout = TimeSpan.FromMinutes(DefaultTimeoutMinutes);
            }

            var start = DateTime.UtcNow;
            var markerFile = System.Environment.ExpandEnvironmentVariables(MarkerFilePath);
            while (start.Add(timeout) >= DateTime.UtcNow)
            {
                // create or update marker timestamp
                OperationManager.SafeExecute(() => File.WriteAllText(markerFile, start.ToString("o")));

                var cancelation = new CancellationTokenSource();
                var delay = Task.Delay(DefaultUpdateMarkerIntervalMS, cancelation.Token);
                var completed = await Task.WhenAny(delay, task);
                if (completed != delay)
                {
                    cancelation.Cancel();
                    break;
                }
            }

            // remove marker
            OperationManager.SafeExecute(() => File.Delete(markerFile));
        }

        private static void ExecuteScript(string file)
        {
            var fi = new FileInfo(file);
            ProcessStartInfo processInfo;
            if (string.Equals(".ps1", fi.Extension, StringComparison.OrdinalIgnoreCase))
            {
                processInfo = new ProcessStartInfo("PowerShell.exe", string.Format("-ExecutionPolicy RemoteSigned -File \"{0}\"", file));
            }
            else
            {
                processInfo = new ProcessStartInfo(file);
            }

            processInfo.CreateNoWindow = true;
            processInfo.UseShellExecute = false;
            processInfo.RedirectStandardInput = true;
            processInfo.RedirectStandardError = true;
            processInfo.RedirectStandardOutput = true;

            DataReceivedEventHandler stdoutHandler = (object sender, DataReceivedEventArgs e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    Trace(TraceEventType.Information, "{0}", e.Data);
                }
            };

            DataReceivedEventHandler stderrHandler = (object sender, DataReceivedEventArgs e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    Trace(TraceEventType.Error, "{0}", e.Data);
                }
            };

            Trace(TraceEventType.Information, "Run post-deployment: \"{0}\" {1}", processInfo.FileName, processInfo.Arguments);
            var process = Process.Start(processInfo);
            var processName = process.ProcessName;
            var processId = process.Id;
            Trace(TraceEventType.Information, "Process {0}({1}) started", processName, processId);

            // hook stdout and stderr
            process.OutputDataReceived += stdoutHandler;
            process.BeginOutputReadLine();
            process.ErrorDataReceived += stderrHandler;
            process.BeginErrorReadLine();

            var timeout = (int)GetCommandTimeOut().TotalMilliseconds;
            if (!process.WaitForExit(timeout))
            {
                process.Kill();
                throw new TimeoutException(String.Format("Process {0}({1}) exceeded {2}ms timeout", processName, processId, timeout));
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(String.Format("Process {0}({1}) exited with {2} exitcode.", processName, processId, process.ExitCode));
            }

            Trace(TraceEventType.Information, "Process {0}({1}) executed successfully.", processName, processId);
        }

        private static TimeSpan GetCommandTimeOut()
        {
            const int DefaultCommandTimeout = 60;

            var val = System.Environment.GetEnvironmentVariable(SettingsKeys.CommandIdleTimeout);
            if (!string.IsNullOrEmpty(val))
            {
                int commandTimeout;
                if (Int32.TryParse(val, out commandTimeout) && commandTimeout > 0)
                {
                    return TimeSpan.FromSeconds(commandTimeout);
                }
            }

            return TimeSpan.FromSeconds(DefaultCommandTimeout);
        }

        private static IEnumerable<string> GetPostBuildActionScripts()
        {
            // "/site/deployments/tools/PostDeploymentActions" (can override with %SCM_POST_DEPLOYMENT_ACTIONS_PATH%)
            // if %SCM_POST_DEPLOYMENT_ACTIONS_PATH% is set, it is absolute path to the post-deployment script folder
            var postDeploymentPath = System.Environment.GetEnvironmentVariable(SettingsKeys.PostDeploymentActionsDirectory);
            if (string.IsNullOrEmpty(postDeploymentPath))
            {
                postDeploymentPath = System.Environment.ExpandEnvironmentVariables(@"%HOME%\site\deployments\tools\PostDeploymentActions");
            }

            if (!Directory.Exists(postDeploymentPath))
            {
                return Enumerable.Empty<string>();
            }

            // Find all post action scripts and order file alphabetically for each folder
            return Directory.GetFiles(postDeploymentPath, "*", SearchOption.TopDirectoryOnly)
                                    .Where(f => f.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                                        || f.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                                        || f.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(n => n);
        }

        private static void Trace(TraceEventType eventType, string message)
        {
            Trace(eventType, "{0}", message);
        }

        private static void Trace(TraceEventType eventType, string format, params object[] args)
        {
            var tracer = _tracer;
            if (tracer != null)
            {
                tracer.TraceEvent(null, "PostDeployment", eventType, (int)eventType, format, args);
            }
        }
    }
}
