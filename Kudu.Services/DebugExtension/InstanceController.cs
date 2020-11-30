using Kudu.Core.K8SE;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Kudu.Services.DebugExtension
{
    [Route("/instances")]
    public class InstanceController : Controller
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        [HttpGet]
        public async Task<List<PodInstance>> GetInstances()
        {
            if(K8SEDeploymentHelper.IsK8SEEnvironment())
            {
                return K8SEDeploymentHelper.GetInstances(K8SEDeploymentHelper.GetAppName(HttpContext));
            }

            return null;
        }

        [Route("{instanceId}/webssh")]
        public async Task<string> SSH(string instanceId)
        {
            if(K8SEDeploymentHelper.IsK8SEEnvironment())
            {
                var instances = K8SEDeploymentHelper.GetInstances(K8SEDeploymentHelper.GetAppName(HttpContext));
                PodInstance instance = null;
                if (instances.Count > 0)
                {
                    instance = instances.Where(i => i.Name.Equals(instanceId, System.StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
                }

                if(instances.Count > 0 && instanceId.Equals("any", System.StringComparison.OrdinalIgnoreCase))
                {
                    instance = instances[0];
                }

                if(instance == null)
                {
                    return "Invalid instance";
                }

                HttpContext.Request.Headers["WEBSHITE_SSH_USER"] = "root";
                HttpContext.Request.Headers["WEBSHITE_SSH_PASSWORD"] = "Docker!";
                HttpContext.Request.Headers["WEBSHITE_SSH_IP"] = instance.IpAddress;

                var targetUri = BuildTargetUri(HttpContext.Request);
                var targetRequestMessage = CreateTargetMessage(HttpContext, targetUri);

                using (var responseMessage = await _httpClient.SendAsync(targetRequestMessage, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted))
                {
                    HttpContext.Response.StatusCode = (int)responseMessage.StatusCode;
                    CopyFromTargetResponseHeaders(HttpContext, responseMessage);
                    await responseMessage.Content.CopyToAsync(HttpContext.Response.Body);
                }
            }
            return null;
        }

        private HttpRequestMessage CreateTargetMessage(HttpContext context, Uri targetUri)
        {
            var requestMessage = new HttpRequestMessage();
            CopyFromOriginalRequestContentAndHeaders(context, requestMessage);

            requestMessage.RequestUri = targetUri;
            requestMessage.Headers.Host = targetUri.Host;
            requestMessage.Method = GetMethod(context.Request.Method);

            return requestMessage;
        }

        private void CopyFromOriginalRequestContentAndHeaders(HttpContext context, HttpRequestMessage requestMessage)
        {
            var requestMethod = context.Request.Method;

            if (!HttpMethods.IsGet(requestMethod) &&
              !HttpMethods.IsHead(requestMethod) &&
              !HttpMethods.IsDelete(requestMethod) &&
              !HttpMethods.IsTrace(requestMethod))
            {
                var streamContent = new StreamContent(context.Request.Body);
                requestMessage.Content = streamContent;
            }

            foreach (var header in context.Request.Headers)
            {
                requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        private void CopyFromTargetResponseHeaders(HttpContext context, HttpResponseMessage responseMessage)
        {
            foreach (var header in responseMessage.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            foreach (var header in responseMessage.Content.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            context.Response.Headers.Remove("transfer-encoding");
        }
        private static HttpMethod GetMethod(string method)
        {
            if (HttpMethods.IsDelete(method)) return HttpMethod.Delete;
            if (HttpMethods.IsGet(method)) return HttpMethod.Get;
            if (HttpMethods.IsHead(method)) return HttpMethod.Head;
            if (HttpMethods.IsOptions(method)) return HttpMethod.Options;
            if (HttpMethods.IsPost(method)) return HttpMethod.Post;
            if (HttpMethods.IsPut(method)) return HttpMethod.Put;
            if (HttpMethods.IsTrace(method)) return HttpMethod.Trace;
            return new HttpMethod(method);
        }

        private Uri BuildTargetUri(HttpRequest request)
        {
            Uri targetUri = null;

            if (request.Path.StartsWithSegments("/webssh", out var remainingPath))
            {
                Console.WriteLine("PATH STRING : " + remainingPath);
                targetUri = new Uri("localhost:3000" + remainingPath);
            }

            return targetUri;
        }
    }
}
