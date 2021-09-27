using Microsoft.AspNetCore.Mvc;
using Kudu.Core.K8SE;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Kudu.Services.Diagnostics
{
    public class ProcessController : Controller
    {
        [HttpGet]
        public async Task<IActionResult> GetAllProcesses([FromQuery] int instance)
        {
            var appNamespace = K8SEDeploymentHelper.GetAppNamespace(HttpContext);
            var appName = K8SEDeploymentHelper.GetAppName(HttpContext);
            using var k8seClient = new K8SEClient();

            // appNamespace = "appservice-ns";
            // appName = "test2";

            var pods = k8seClient.GetPodsForDeployment(appNamespace, appName);
            if (pods == null || pods.Count == 0)
            {
                return BadRequest($"No pod found for the app '{appName}'");
            }

            if (instance >= pods.Count || instance < 0)
            {
                return BadRequest($"Instance index error, valid values are [0, {pods.Count}]");
            }

            // For command with params, it should split into command list
            var command = new List<string>()
            {
                "ps",
                "-aux"
            };

            var result = await k8seClient.ExecuteCommandInPodAsync(appNamespace, pods[instance].Name, command);
            return Ok(result);
        }
    }
}
