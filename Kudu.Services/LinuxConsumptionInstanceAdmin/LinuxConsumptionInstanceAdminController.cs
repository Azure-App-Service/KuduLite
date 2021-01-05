using Kudu.Services.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Kudu.Contracts.Settings;
using Kudu.Core.Helpers;
using System.Collections.Generic;
using Kudu.Core.LinuxConsumption;
using Kudu.Services.Infrastructure.Authorization;

namespace Kudu.Services.LinuxConsumptionInstanceAdmin
{
    /// <summary>
    /// It is mainly responsible for integrating KuduLite with Azure Functions Linux Consumption Plan.
    /// There are two endpoints we need to provide in order to propagate container information and perform specialization.
    /// </summary>
    public class LinuxConsumptionInstanceAdminController : Controller
    {
        private const string _appsettingPrefix = "APPSETTING_";
        private readonly ILinuxConsumptionInstanceManager _instanceManager;
        private readonly IDeploymentSettingsManager _settingsManager;
        private readonly IMeshPersistentFileSystem _meshPersistentFileSystem;

        /// <summary>
        /// This class handles appsetting assignment and provide information when running in a Service Fabric Mesh container,
        /// namely, Functionapp on Linux Consumption Plan.
        /// </summary>
        /// <param name="instanceManager">Allow KuduLite to interact with Service Fabric Mesh instance in Linux Consumption</param>
        /// <param name="settingsManager">Allow instance assignment to change application setting</param>
        /// <param name="meshPersistentFileSystem">Provides persistent storage for Linux Consumption</param>
        public LinuxConsumptionInstanceAdminController(ILinuxConsumptionInstanceManager instanceManager, IDeploymentSettingsManager settingsManager, IMeshPersistentFileSystem meshPersistentFileSystem)
        {
            _instanceManager = instanceManager;
            _settingsManager = settingsManager;
            _meshPersistentFileSystem = meshPersistentFileSystem;
        }

        /// <summary>
        /// A healthcheck endpoint
        /// </summary>
        /// <returns>Expect 200 when current service is up and running</returns>
        [HttpGet]
        [Authorize(Policy = AuthPolicyNames.AdminAuthLevel)]
        public IActionResult Info()
        {
            var instanceHealth = new InstanceHealth {Items = new List<InstanceHealthItem>()};
            var fileShareMount = new InstanceHealthItem();
            fileShareMount.Name = "Persistent storage";
            fileShareMount.Success = _meshPersistentFileSystem.GetStatus(out var persistenceMessage);
            fileShareMount.Message = persistenceMessage;
            instanceHealth.Items.Add(fileShareMount);

            return Ok(instanceHealth);
        }

        /// <summary>
        /// A specialization endpoint.
        /// It sets the current service's site name, site id and environment variables when it is called.
        /// </summary>
        /// <param name="encryptedAssignmentContext">Encrypted content which contains HostAssignmentContext</param>
        /// <returns>Expect 202 when receives the first call, otherwise, returns 409</returns>
        [HttpPost]
        [Authorize(Policy = AuthPolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> AssignAsync([FromBody] EncryptedHostAssignmentContext encryptedAssignmentContext)
        {
            var containerKey = System.Environment.GetEnvironmentVariable(SettingsKeys.ContainerEncryptionKey);
            var assignmentContext = encryptedAssignmentContext.Decrypt(containerKey);

            // We don't need to do validation on run_from_package zip, scm site on Linux Consumption should always be on
            var assignmentResult = _instanceManager.StartAssignment(assignmentContext);

            // modify app settings from environment variables (for all start with "APPSETTING_")
            // setting APPSETTING_FUNCTIONS_EXTENSION_VERSION will both set the appsetting and environment variable
            if (assignmentResult)
            {
                foreach (KeyValuePair<string, string> env in assignmentContext.Environment)
                {
                    string key = env.Key;
                    string value = env.Value;

                    if (key.StartsWith(_appsettingPrefix))
                    {
                        key = key.Substring(_appsettingPrefix.Length);
                        _settingsManager.SetValue(key, value);
                    }

                    // configure function app specialization properly for Linux Consumption plan
                    Dictionary<string, string> newSettings = FunctionAppSpecializationHelper.HandleLinuxConsumption(key, value);
                    foreach (KeyValuePair<string, string> newSetting in newSettings)
                    {
                        if (System.Environment.GetEnvironmentVariable(newSetting.Key) == null)
                        {
                            System.Environment.SetEnvironmentVariable(newSetting.Key, newSetting.Value);
                        }

                        if (_settingsManager.GetValue(newSetting.Key) == null)
                        {
                            _settingsManager.SetValue(newSetting.Key, newSetting.Value);
                        }
                    }
                }
            }

            return assignmentResult
               ? Accepted()
               : StatusCode(StatusCodes.Status409Conflict, "Instance already assigned");
        }

        /// <summary>
        /// A http health endpoint for liveness probe. 
        /// </summary>
        [HttpGet]
        public IActionResult HttpHealthCheck()
        {
            return Ok();
        }

        /// <summary>
        /// Returns eviction status of the container. Currently no-op.
        /// </summary>
        [HttpGet]
        [Authorize(Policy = AuthPolicyNames.AdminAuthLevel)]
        public IActionResult EvictionStatus()
        {
            return Json(false);
        }
    }
}