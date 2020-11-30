using Kudu.Core.K8SE;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kudu.Services.DebugExtension
{
    [Route("/instances")]
    public class InstanceController : Controller
    {
        [HttpGet]
        public async Task<List<PodInstance>> GetInstances()
        {
            if(K8SEDeploymentHelper.IsK8SEEnvironment())
            {
                return K8SEDeploymentHelper.GetInstances(K8SEDeploymentHelper.GetAppName(HttpContext));
            }

            return null;
        }
    }
}
