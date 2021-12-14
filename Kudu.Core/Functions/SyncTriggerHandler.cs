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
        private readonly IDictionary<string, string> _appSettings;

        public SyncTriggerHandler(IEnvironment environment,
            ITracer tracer,
            IDictionary<string, string> appSettings)
        {
            _environment = environment;
            _tracer = tracer;
            _appSettings = appSettings ?? new Dictionary<string, string>();
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

                await Task.Run(() => K8SEDeploymentHelper.UpdateFunctionAppTriggers(appName, scaleTriggers, null));
            }

            return null;
        }

        public Tuple<IEnumerable<ScaleTrigger>, string> GetScaleTriggers(string functionTriggersPayload)
        {
            IEnumerable<ScaleTrigger> scaleTriggers = new List<ScaleTrigger>();
            try
            {
                if (string.IsNullOrEmpty(functionTriggersPayload))
                {
                    return new Tuple<IEnumerable<ScaleTrigger>, string>(null, "Function trigger payload is null or empty.");
                }

                string appName = _environment.K8SEAppName;
                string appNamespace = _environment.K8SEAppNamespace;
                scaleTriggers =
                    KedaFunctionTriggerProvider.GetFunctionTriggersFromSyncTriggerPayload(appName, appNamespace, functionTriggersPayload);
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
