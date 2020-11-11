using Kudu.Contracts.Deployment;
using Kudu.Contracts.Tracing;
using Kudu.Core.K8SE;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kudu.Core.Tracing;

namespace Kudu.Core.Functions
{
    public class SyncTriggerHandler
    {
        private readonly IEnvironment _environment;
        private readonly ITracer _tracer;

        public SyncTriggerHandler(IEnvironment environment,
            ITracer tracer)
        {
            _environment = environment;
            _tracer = tracer;
        }

        public async Task<string> SyncTriggers(string functionTriggersPayload)
        {
            using (_tracer.Step("SyncTriggerHandler.SyncTrigger()"))
            {
                var scaleTriggersContent = GetScaleTriggers(functionTriggersPayload);
                if (!string.IsNullOrEmpty(scaleTriggersContent.Item2))
                {
                    return scaleTriggersContent.Item2;
                }

                var scaleTriggers = scaleTriggersContent.Item1;
                string appName = _environment.K8SEAppName;
                string buildNumber = Guid.NewGuid().ToString();
                var buildMetadata = new BuildMetadata()
                {
                    AppName = appName,
                    BuildVersion = buildNumber,
                    AppSubPath = string.Empty
                };

                await Task.Run(() => K8SEDeploymentHelper.UpdateFunctionAppTriggers(appName, scaleTriggers, buildMetadata));
            }

            return null;
        }

        public Tuple<IEnumerable<ScaleTrigger>, string> GetScaleTriggers(string functionTriggersPayload)
        {
            var scaleTriggers = new List<ScaleTrigger>();
            try
            {
                if (string.IsNullOrEmpty(functionTriggersPayload))
                {
                    return new Tuple<IEnumerable<ScaleTrigger>, string>(null, "Function trigger payload is null or empty.");
                }

                var triggersJson = JArray.Parse(functionTriggersPayload);
                foreach (JObject trigger in triggersJson)
                {
                    var type = (string)trigger["type"];
                    if (type.EndsWith("Trigger", StringComparison.OrdinalIgnoreCase))
                    {
                        var scaleTrigger = new ScaleTrigger
                        {
                            Type = KedaFunctionTriggerProvider.GetKedaTriggerType(type),
                            Metadata = new Dictionary<string, string>()
                        };

                        foreach (var property in trigger)
                        {
                            if (property.Value.Type == JTokenType.String)
                            {
                                scaleTrigger.Metadata.Add(property.Key, property.Value.ToString());
                            }
                        }

                        scaleTriggers.Add(scaleTrigger);
                    }
                }

                if (!scaleTriggers.Any())
                {
                    return new Tuple<IEnumerable<ScaleTrigger>, string>(null, "No triggers in the payload");
                }
            }
            catch (Exception e)
            {
                return new Tuple<IEnumerable<ScaleTrigger>, string>(null, e.Message);
            }

            return new Tuple<IEnumerable<ScaleTrigger>, string>(scaleTriggers, null); ;
        }
    }
}
