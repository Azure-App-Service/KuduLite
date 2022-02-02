using Kudu.Core.Helpers;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Kudu.Core.K8SE;

namespace Kudu.Core.Functions
{
    public static class KedaFunctionTriggerProvider
    {
        public static IEnumerable<ScaleTrigger> GetFunctionTriggers(string zipFilePath, string appName = null, string appType = null, IDictionary<string, string> appSettings = null)
        {
            appSettings = appSettings ?? new Dictionary<string, string>();

            if (!File.Exists(zipFilePath))
            {
                return null;
            }

            string hostJsonText = null;
            var triggerBindings = new List<FunctionTrigger>();
            using (var zip = ZipFile.OpenRead(zipFilePath))
            {
                var hostJsonEntry = zip.Entries.FirstOrDefault(e => IsHostJson(e.FullName));
                if (hostJsonEntry != null)
                {
                    using (var reader = new StreamReader(hostJsonEntry.Open()))
                    {
                        hostJsonText = reader.ReadToEnd();
                    }
                }

                var entries = zip.Entries
                    .Where(e => IsFunctionJson(e.FullName));

                foreach (var entry in entries)
                {
                    using (var stream = entry.Open())
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            triggerBindings.AddRange(ParseFunctionJson(GetFunctionName(entry), JObject.Parse(reader.ReadToEnd())));
                        }
                    }
                }
            }

            bool IsFunctionJson(string fullName)
            {
                return fullName.EndsWith(Constants.FunctionsConfigFile) &&
                       fullName.Count(c => c == '/' || c == '\\') == 1;
            }

            bool IsHostJson(string fullName)
            {
                return fullName.Equals(Constants.FunctionsHostConfigFile, StringComparison.OrdinalIgnoreCase);
            }

            var triggers = CreateScaleTriggers(triggerBindings, hostJsonText, appSettings).ToList();

            var isWorkflowApp = appType?.ToLowerInvariant()?.Contains(Constants.WorkflowAppKind.ToLowerInvariant());
            if (isWorkflowApp.GetValueOrDefault(defaultValue: false))
            {
                // NOTE(haassyad) Check if the host json has the workflow extension loaded. If so we will add a queue scale trigger for the job dispatcher queue.
                if (TryGetWorkflowKedaTrigger(hostJsonText, appName, out ScaleTrigger workflowScaleTrigger))
                {
                    triggers.Add(workflowScaleTrigger);
                }
            }

            return triggers;
        }

        internal static void UpdateFunctionTriggerBindingExpression(
            IEnumerable<ScaleTrigger> scaleTriggers, IDictionary<string, string> appSettings)
        {
            string ReplaceMatchedBindingExpression(Match match)
            {
                var bindingExpressionTarget = match.Value.Replace("%", "");
                if (appSettings.ContainsKey(bindingExpressionTarget))
                {
                    return appSettings[bindingExpressionTarget];
                }

                return bindingExpressionTarget;
            }

            var matchEvaluator = new MatchEvaluator((Func<Match, string>)ReplaceMatchedBindingExpression);

            foreach (var scaleTrigger in scaleTriggers)
            {
                IDictionary<string, string> newMetadata = new Dictionary<string, string>();
                foreach (var metadata in scaleTrigger.Metadata)
                {
                    var replacedValue = Regex.Replace(metadata.Value, Constants.AppSettingsRegex, matchEvaluator);
                    newMetadata[metadata.Key] = replacedValue;
                }

                scaleTrigger.Metadata = newMetadata;
            }
        }

        public static IEnumerable<ScaleTrigger> GetFunctionTriggers(IEnumerable<JObject> functionsJson, string hostJsonText, IDictionary<string, string> appSettings)
        {
            var triggerBindings = functionsJson
                .Select(o => ParseFunctionJson(o["functionName"]?.ToString(), o))
                .SelectMany(i => i);

            return CreateScaleTriggers(triggerBindings, hostJsonText, appSettings);
        }

        public static IEnumerable<ScaleTrigger> GetFunctionTriggersFromSyncTriggerPayload(string synctriggerPayload,
            IDictionary<string, string> appSettings)
        {
            return CreateScaleTriggers(ParseSyncTriggerPayload(synctriggerPayload), ParseHostJsonPayload(synctriggerPayload), appSettings);
        }

        internal static IEnumerable<ScaleTrigger> CreateScaleTriggers(IEnumerable<FunctionTrigger> triggerBindings, string hostJsonText, IDictionary<string, string> appSettings)
        {

            var durableTriggers = triggerBindings.Where(b => IsDurable(b));
            var standardTriggers = triggerBindings.Where(b => !IsDurable(b));

            var kedaScaleTriggers = new List<ScaleTrigger>();
            kedaScaleTriggers.AddRange(GetStandardScaleTriggers(standardTriggers));

            // Update Binding Expression for %..% notation
            UpdateFunctionTriggerBindingExpression(kedaScaleTriggers, appSettings);

            // Durable Functions triggers are treated as a group and get configuration from host.json
            if (durableTriggers.Any() && TryGetDurableKedaTrigger(hostJsonText, out ScaleTrigger durableScaleTrigger))
            {
                kedaScaleTriggers.Add(durableScaleTrigger);
            }

            bool IsDurable(FunctionTrigger function) =>
                function.Type.Equals("orchestrationTrigger", StringComparison.OrdinalIgnoreCase) ||
                function.Type.Equals("activityTrigger", StringComparison.OrdinalIgnoreCase) ||
                function.Type.Equals("entityTrigger", StringComparison.OrdinalIgnoreCase);

            return kedaScaleTriggers;
        }

        internal static string ParseHostJsonPayload(string payload)
        {
            var payloadJson = JObject.Parse(payload);
            var extensions = (JObject)payloadJson["extensions"];
            if (extensions != null)
            {
                var hostJsonPayload = new JObject { { "extensions", extensions } };
                return hostJsonPayload.ToString();
            }
            else
            {
                return string.Empty;
            }
        }

        internal static IEnumerable<FunctionTrigger> ParseSyncTriggerPayload(string payload)
        {
            var payloadJson = JObject.Parse(payload);
            var triggers = (JArray)payloadJson["triggers"];
            return triggers.Select(o => o.ToObject<JObject>())
                .Select(o => new FunctionTrigger(o["functionName"].ToString(), o, o["type"].ToString()));
        }

        internal static IEnumerable<FunctionTrigger> ParseFunctionJson(string functionName, JObject functionJson)
        {
            if (functionJson.TryGetValue("disabled", out JToken value))
            {
                string stringValue = value.ToString();
                if (!bool.TryParse(stringValue, out bool disabled))
                {
                    string expandValue = System.Environment.GetEnvironmentVariable(stringValue);
                    disabled = string.Equals(expandValue, "1", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(expandValue, "true", StringComparison.OrdinalIgnoreCase);
                }

                if (disabled)
                {
                    yield break;
                }
            }

            var excluded = functionJson.TryGetValue("excluded", out value) && (bool)value;
            if (excluded)
            {
                yield break;
            }

            foreach (JObject binding in (JArray)functionJson["bindings"])
            {
                var type = (string)binding["type"];
                if (type.EndsWith("Trigger", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new FunctionTrigger(functionName, binding, type);
                }
            }
        }

        internal static IEnumerable<ScaleTrigger> GetStandardScaleTriggers(IEnumerable<FunctionTrigger> standardTriggers)
        {
            foreach (FunctionTrigger function in standardTriggers)
            {
                var triggerType = GetKedaTriggerType(function.Type);
                if (!string.IsNullOrEmpty(triggerType))
                {
                    var scaleTrigger = new ScaleTrigger();
                    scaleTrigger.Type = triggerType;
                    scaleTrigger.Metadata = PopulateMetadataDictionary(function.Binding, function.FunctionName);

                    IKedaAuthRefProvider authProvider = getTriggerAuthProvider(triggerType);
                    if (authProvider != null)
                    {
                        scaleTrigger.AuthenticationRef = authProvider.PopulateAuthenticationRef(function.Binding, function.FunctionName);
                    }

                    yield return scaleTrigger;
                }
            }
        }

        internal static IKedaAuthRefProvider getTriggerAuthProvider(string triggerType)
        {
            if (string.Equals(triggerType, "kafka", StringComparison.OrdinalIgnoreCase)) {
                return new KafkaTriggerKedaAuthProvider();
            }
            return null;
        }

        internal static string GetFunctionName(ZipArchiveEntry zipEntry)
        {
            if (string.IsNullOrWhiteSpace(zipEntry?.FullName))
            {
                return string.Empty;
            }

            return zipEntry.FullName.Split('/').Length == 2 ? zipEntry.FullName.Split('/')[0] : zipEntry.FullName.Split('\\')[0];
        }

        internal static string GetKedaTriggerType(string triggerType)
        {
            if (string.IsNullOrEmpty(triggerType))
            {
                throw new ArgumentNullException(nameof(triggerType));
            }

            return triggerType.ToLower() switch
            {
                TriggerTypes.AzureBlobStorage => "azure-blob",
                TriggerTypes.AzureCosmosDb => "azure-cosmosdb",
                TriggerTypes.AzureEventHubs => "azure-eventhub",
                TriggerTypes.AzureServiceBus => "azure-servicebus",
                TriggerTypes.AzureStorageQueue => "azure-queue",
                TriggerTypes.Http => "httpTrigger",
                TriggerTypes.Kafka => "kafka",
                TriggerTypes.RabbitMq => "rabbitmq",
                _ => string.Empty,
            };
        }

        internal static bool TryGetDurableKedaTrigger(string hostJsonText, out ScaleTrigger scaleTrigger)
        {
            scaleTrigger = null;
            if (string.IsNullOrEmpty(hostJsonText))
            {
                return false;
            }

            JObject hostJson = JObject.Parse(hostJsonText);

            // Reference: https://docs.microsoft.com/azure/azure-functions/durable/durable-functions-bindings#durable-functions-2-0-host-json
            string durableStorageProviderPath = $"{Constants.Extensions}.{Constants.DurableTask}.{Constants.DurableTaskStorageProvider}";
            JObject storageProviderConfig = hostJson.SelectToken(durableStorageProviderPath) as JObject;
            string storageType = storageProviderConfig?["type"]?.ToString();

            // Custom storage types are supported starting in Durable Functions v2.4.2
            // Minimum required version of Microsoft.DurableTask.SqlServer.AzureFunctions is v0.7.0-alpha
            if (string.Equals(storageType, Constants.DurableTaskMicrosoftSqlProviderType, StringComparison.OrdinalIgnoreCase))
            {
                scaleTrigger = new ScaleTrigger
                {
                    // MSSQL scaler reference: https://keda.sh/docs/2.2/scalers/mssql/
                    Type = Constants.MicrosoftSqlScaler,
                    Metadata = new Dictionary<string, string>
                    {
                        // Durable SQL scaling: https://microsoft.github.io/durabletask-mssql/#/scaling?id=worker-auto-scale
                        ["query"] = "SELECT dt.GetScaleRecommendation(10, 1)", // max 10 orchestrations and 1 activity per replica
                        ["targetValue"] = "1",
                        ["connectionStringFromEnv"] = storageProviderConfig?[Constants.DurableTaskSqlConnectionName]?.ToString(),
                    }
                };
            }
            else
            {
                // TODO: Support for the Azure Storage and Netherite backends
            }

            return scaleTrigger != null;
        }

        /// <summary>
        /// Tries to add a scale trigger if the app is a workflow app.
        /// </summary>
        /// <param name="hostJsonText">The host.json text.</param>
        /// <param name="appName">The app name.</param>
        /// <param name="scaleTrigger">The scale trigger.</param>
        /// <returns>true if a scale trigger was found</returns>
        internal static bool TryGetWorkflowKedaTrigger(string hostJsonText, string appName, out ScaleTrigger scaleTrigger)
        {
            JObject hostJson = JObject.Parse(hostJsonText);

            // Check the host.json file for workflow settings.
            JObject workflowSettings = hostJson
                .SelectToken(path: $"{Constants.Extensions}.{Constants.WorkflowExtensionName}.{Constants.WorkflowSettingsName}") as JObject;

            // Get the queue length if specified, otherwise default to arbitrary value.
            var queueLengthObject = workflowSettings?["Runtime.ScaleMonitor.KEDA.TargetQueueLength"];
            var queueLength = queueLengthObject != null ? queueLengthObject.ToString() : "20";

            // Get the host id if specified, otherwise default to app name.
            var hostIdObject = workflowSettings?["Runtime.HostId"];
            var hostId = hostIdObject != null ? hostIdObject.ToString() : appName;

            // Hash the host id.
            var hostSpecificStorageId = StringHelper
                .EscapeAndTrimStorageKeyPrefix(HashHelper.MurmurHash64(hostId).ToString("X"), 32)
                .ToLowerInvariant();

            var queuePrefix = $"flow{hostSpecificStorageId}jobtriggers";

            scaleTrigger = new ScaleTrigger
            {
                // Azure queue scaler reference: https://keda.sh/docs/2.2/scalers/azure-storage-queue/
                Type = Constants.AzureQueueScaler,
                Metadata = new Dictionary<string, string>
                {
                    // NOTE(haassyad): We only have one queue partition in single tenant.
                    ["queueName"] = StringHelper.GetWorkflowQueueNameInternal(queuePrefix, 1),
                    ["queueLength"] = queueLength,
                    ["connectionFromEnv"] = "AzureWebJobsStorage",
                }
            };

            return true;
        }

        // match https://github.com/Azure/azure-functions-core-tools/blob/6bfab24b2743f8421475d996402c398d2fe4a9e0/src/Azure.Functions.Cli/Kubernetes/KEDA/V2/KedaV2Resource.cs#L91
        internal static IDictionary<string, string> PopulateMetadataDictionary(JToken t, string functionName)
        {
            const string ConnectionField = "connection";
            const string ConnectionFromEnvField = "connectionFromEnv";

            IDictionary<string, string> metadata = t.ToObject<Dictionary<string, JToken>>()
                .Where(i => i.Value.Type == JTokenType.String)
                .ToDictionary(k => k.Key, v => v.Value.ToString());

            var triggerType = t["type"].ToString().ToLower();

            switch (triggerType)
            {
                case TriggerTypes.AzureBlobStorage:
                case TriggerTypes.AzureStorageQueue:
                    metadata[ConnectionFromEnvField] = metadata[ConnectionField] ?? "AzureWebJobsStorage";
                    metadata.Remove(ConnectionField);
                    break;
                case TriggerTypes.AzureServiceBus:
                    metadata[ConnectionFromEnvField] = metadata[ConnectionField] ?? "AzureWebJobsServiceBus";
                    metadata.Remove(ConnectionField);
                    break;
                case TriggerTypes.AzureEventHubs:
                    metadata[ConnectionFromEnvField] = metadata[ConnectionField];
                    metadata.Remove(ConnectionField);
                    break;

                case TriggerTypes.Kafka:
                    metadata["bootstrapServersFromEnv"] = metadata["brokerList"];
                    metadata.Remove("brokerList");
                    metadata.Remove("protocol");
                    metadata.Remove("authenticationMode");
                    break;

                case TriggerTypes.RabbitMq:
                    metadata["hostFromEnv"] = metadata["connectionStringSetting"];
                    metadata.Remove("connectionStringSetting");
                    break;

                case TriggerTypes.AzureCosmosDb:

                    // Following code supports CosmosDBTrigger binding fields as defined in both v3 and v4 versions of
                    // Microsoft.Azure.WebJobs.Extensions.CosmosDB package. Also, it places default values for optional
                    // fields to match the logic in extension library (https://github.com/Azure/azure-webjobs-sdk-extensions).

                    const string DefaultConnectionStringName = "CosmosDB";
                    const string DefaultLeaseCollectionName = "leases";

                    metadata = new Dictionary<string, string>
                    {
                        // Not including 'scalerAddress' field since it can vary with environment.
                        ["connection"] = metadata.GetValue("connection", "connectionStringSetting") ?? DefaultConnectionStringName,
                        ["databaseId"] = metadata.GetValue("databaseName"),
                        ["containerId"] = metadata.GetValue("containerName", "collectionName"),
                        ["leaseConnection"] = metadata.GetValue("leaseConnection", "connection", "leaseConnectionStringSetting", "connectionStringSetting") ?? DefaultConnectionStringName,
                        ["leaseDatabaseId"] = metadata.GetValue("leaseDatabaseName", "databaseName"),
                        ["leaseContainerId"] = metadata.GetValue("leaseContainerName", "leaseCollectionName") ?? DefaultLeaseCollectionName,
                        ["processorName"] = metadata.GetValue("leaseContainerPrefix", "leaseCollectionPrefix") ?? string.Empty,
                    };

                    break;
            }

            // Clean-up for all triggers

            metadata.Remove("type");
            metadata.Remove("name");

            metadata["functionName"] = functionName;
            return metadata;
        }

        private static string GetValue(this IDictionary<string, string> dictionary, params string[] keys)
        {
            string containedKey = keys.FirstOrDefault(key => dictionary.ContainsKey(key));
            return containedKey != null ? dictionary[containedKey] : null;
        }

        internal class FunctionTrigger
        {
            public FunctionTrigger(string functionName, JObject binding, string type)
            {
                this.FunctionName = functionName;
                this.Binding = binding;
                this.Type = type;
            }

            public string FunctionName { get; }
            public JObject Binding { get; }
            public string Type { get; }
        }

        static class TriggerTypes
        {
            public const string AzureBlobStorage = "blobtrigger";
            public const string AzureCosmosDb = "cosmosdbtrigger";
            public const string AzureEventHubs = "eventhubtrigger";
            public const string AzureServiceBus = "servicebustrigger";
            public const string AzureStorageQueue = "queuetrigger";
            public const string Http = "httptrigger";
            public const string Kafka = "kafkatrigger";
            public const string RabbitMq = "rabbitmqtrigger";
        }
    }
}
    

