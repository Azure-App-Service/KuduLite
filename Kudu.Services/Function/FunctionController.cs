using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Functions;
using Kudu.Core.K8SE;
using Kudu.Core.Kube;
using Kudu.Core.Tracing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Remotion.Linq.Parsing.Structure.IntermediateModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kudu.Services.Function
{
    public class FunctionController : Controller
    {
        private IEnvironment _environment;
        private readonly IAnalytics _analytics;
        private readonly ITracer _tracer;

        public FunctionController(ITracer tracer,
            IAnalytics analytics,
            IHttpContextAccessor accessor)
        {
            _tracer = tracer;
            _environment = (IEnvironment)accessor.HttpContext.Items["environment"];
            _analytics = analytics;
        }

        /// <summary>
        /// Sync triggers to the k8se function apps
        /// </summary>
        /// <returns></returns>
        [HttpPut]
        public async Task<IActionResult> SyncTrigger()
        {
            try
            {
                var headers = Request.Headers.ToDictionary(a => a.Key, a => a.Value.AsEnumerable());
                var authResult = await SyncTriggerAuthenticator.AuthenticateCaller(headers);
                if (!authResult)
                {
                    return StatusCode(StatusCodes.Status401Unauthorized, "Invalid authetication token in the header");
                }
            }
            catch (Exception e)
            {
                return StatusCode(StatusCodes.Status401Unauthorized, e.Message);
            }


            IActionResult result = Ok();
            using (_tracer.Step("FunctionController.SyncTrigger()"))
            {
                var triggerPayload = default(string);
                try
                {
                    triggerPayload = await GetRequestBodyPayload();
                }
                catch (Exception e)
                {
                    result = BadRequest(e.ToString());
                    return result;
                }

                try
                {
                    var syncTriggerHandler = new SyncTriggerHandler(_environment, _tracer);
                    var errorMessage = await syncTriggerHandler.SyncTriggers(triggerPayload);
                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        result = BadRequest(errorMessage);
                        return result;
                    }
                }
                catch (Exception e)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, e.ToString());
                }
            }

            return result;
        }

        private async Task<string> GetRequestBodyPayload()
        {
            using (StreamReader reader = new StreamReader(Request.Body))
            {
                return await reader.ReadToEndAsync();
            }
        }
    }
}
