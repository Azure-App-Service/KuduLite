using Kudu.Core.K8SE;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Kudu.Services.Web
{
    public class InstanceMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler()
        {
            AllowAutoRedirect = false,
            MaxConnectionsPerServer = int.MaxValue,
            UseCookies = false,
        });

        private const string CDN_HEADER_NAME = "Cache-Control";
        private static readonly string[] NotForwardedHttpHeaders = new[] { "Connection", "Host" };

        static Regex rx = new Regex(@"^(\/instances\/)([A-Z0-9a-z\-]*)(\/)(.*)$",
          RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public InstanceMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            Console.WriteLine($"Instance Middleware");

            if (context.Request.Path.Value.StartsWith("/instances/", StringComparison.OrdinalIgnoreCase)
                && context.Request.Path.Value.IndexOf("/webssh") < 0)
            {
                Console.WriteLine($"Getting target URI");
                var targetUri = await RewriteInstanceUri(context);
                Console.WriteLine($"Got target URI: {targetUri}");

                if (targetUri != null)
                {
                    var requestMessage = GenerateProxifiedRequest(context, targetUri);
                    await SendAsync(context, requestMessage);

                    return;
                }
            }

            await _next(context);
        }

        private async Task SendAsync(HttpContext context, HttpRequestMessage requestMessage)
        {
            using (var responseMessage = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted))
            {
                context.Response.StatusCode = (int)responseMessage.StatusCode;

                foreach (var header in responseMessage.Headers)
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }

                foreach (var header in responseMessage.Content.Headers)
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }

                context.Response.Headers.Remove("transfer-encoding");

                if (!context.Response.Headers.ContainsKey(CDN_HEADER_NAME))
                {
                    context.Response.Headers.Add(CDN_HEADER_NAME, "no-cache, no-store");
                }

                await responseMessage.Content.CopyToAsync(context.Response.Body);
            }
        }

        private static HttpRequestMessage GenerateProxifiedRequest(HttpContext context, Uri targetUri)
        {
            var requestMessage = new HttpRequestMessage();
            CopyRequestContentAndHeaders(context, requestMessage);

            requestMessage.RequestUri = targetUri;
            requestMessage.Headers.Host = targetUri.Host;
            requestMessage.Method = GetMethod(context.Request.Method);


            return requestMessage;
        }

        private static void CopyRequestContentAndHeaders(HttpContext context, HttpRequestMessage requestMessage)
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
                if (!NotForwardedHttpHeaders.Contains(header.Key))
                {
                    if (header.Key != "User-Agent")
                    {
                        if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && requestMessage.Content != null)
                        {
                            requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                        }
                    }
                    else
                    {
                        string userAgent = header.Value.Count > 0 ? (header.Value[0] + " " + context.TraceIdentifier) : string.Empty;

                        if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, userAgent) && requestMessage.Content != null)
                        {
                            requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, userAgent);
                        }
                    }

                }
            }
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

        public Task<Uri> RewriteInstanceUri(HttpContext context)
        {
            Match m = rx.Match(context.Request.Path);
            if (m.Success && m.Groups.Count >= 4)
            {
                // Find matches.

                var instanceId = m.Groups[2].Value;
                var remainingPath = m.Groups[4].Value;
                Console.WriteLine(instanceId);
                Console.WriteLine(remainingPath);
                List<PodInstance> instances = K8SEDeploymentHelper.GetInstances(K8SEDeploymentHelper.GetAppName(context));
                PodInstance instance = instances.Where(i => i.Name.Equals(instanceId, System.StringComparison.OrdinalIgnoreCase)).FirstOrDefault(); ;

                // handle null, 0 instances
                if(string.IsNullOrEmpty(remainingPath))
                {
                    return null;
                }
                
                if(instances == null || instances.Count == 0)
                {
                    throw new ArgumentOutOfRangeException($"Instance '{instanceId}' not found");
                }

                if (instances.Count > 0 && instanceId.Equals("any", System.StringComparison.OrdinalIgnoreCase))
                {
                    instance = instances[0];
                }

                var newUri = $"http://{instance.IpAddress}:1601/{remainingPath}{context.Request.QueryString}";
                Console.WriteLine($"URI: http://{instance.IpAddress}:1601/{remainingPath}{context.Request.QueryString}");
                var targetUri = new Uri(newUri);
                return Task.FromResult(targetUri);
            }
            else
            {
                Console.WriteLine($"Different count {m.Groups.Count} , Success? {m.Success}");
            }

            return Task.FromResult((Uri)null);
        }
    }
}
