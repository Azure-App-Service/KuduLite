using System;
using Kudu.Core.Helpers;
using Kudu.Core.Tracing;
using Kudu.Contracts.Settings;
using Kudu.Core.Infrastructure;
using Kudu.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Kudu.Core.K8SE;

namespace Kudu.Services.Docker
{
    public class DockerController : Controller
    {
        private const string RESTART_REASON = "Docker CI webhook";
        private readonly ITraceFactory _traceFactory;
        private readonly IDeploymentSettingsManager _settings;
        private readonly IEnvironment _environment;

        public DockerController(ITraceFactory traceFactory, IDeploymentSettingsManager settings, IEnvironment environment)
        {
            _traceFactory = traceFactory;
            _settings = settings;
            if (!K8SEDeploymentHelper.IsK8SEEnvironment())
            {
                _environment = environment;
            }
            else
            {
                _environment = (IEnvironment)HttpContext.Items["environment"];
            }
        }

        [HttpPost]
        public IActionResult ReceiveHook()
        {
            if (OSDetector.IsOnWindows() && !EnvironmentHelper.IsWindowsContainers())
            {
                return StatusCode(StatusCodes.Status404NotFound);
            }

            var tracer = _traceFactory.GetTracer();
            using (tracer.Step("Docker.ReceiveWebhook"))
            {
                try
                {
                    if (_settings.IsDockerCiEnabled())
                    {
                        DockerContainerRestartTrigger.RequestContainerRestart(_environment, RESTART_REASON);
                    }
                }
                catch (Exception e)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, e.Message);
                }
            }

            return Ok();
        }
    }
}