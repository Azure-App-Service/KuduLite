using Kudu.Services.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Kudu.Contracts.Settings;
using System.Collections.Generic;
using Kudu.Services.Infrastructure.Authorization;

namespace Kudu.Services.LinuxConsumptionInstanceAdmin
{
    /// <summary>
    /// InstanceController is mainly responsible for integrating KuduLite with Azure Functions.
    /// There are two endpoints we need to provide in order to propagate container information and perform specialization.
    /// </summary>
    public class LinuxConsumptionInstanceAdminController : Controller
    {
        private readonly IDeploymentSettingsManager _settingsManager;

        /// <summary>
        /// This class handles appsetting assignment and provide information when running in a Service Fabric Mesh container,
        /// namely, Functionapp on Linux Consumption Plan.
        /// </summary>
        /// <param name="settingsManager">Allow instance assignment to change application setting</param>
        public LinuxConsumptionInstanceAdminController(IDeploymentSettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
        }

        /// <summary>
        /// A healthcheck endpoint
        /// </summary>
        /// <returns>Expect 200 when current service is up and running</returns>
        [HttpGet]
        [Authorize(Policy = AuthPolicyNames.AdminAuthLevel)]
        public IActionResult Info()
        {
            return Ok("Endpoint is not implemented");
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
            return Accepted("Endpoint is not implemented");
        }
    }
}
