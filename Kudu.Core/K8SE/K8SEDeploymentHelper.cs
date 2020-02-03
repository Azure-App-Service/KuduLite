using k8s;
using Kudu.Contracts.K8SE;
using Kudu.Contracts.Tracing;
using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Rest;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Kudu.Core.K8SE
{
    public static class K8SEDeploymentHelper
    {

        public static ITracer _tracer;
        private static Kubernetes _client;

        private static Kubernetes _k8seClient
        {
            get
            {
                if (_client == null)
                {

                    var config = KubernetesClientConfiguration.InClusterConfig();
                    _client = new Kubernetes(config);
                }
                return _client;
            }
        }

        // K8SE_BUILD_SERVICE not null or empty
        public static bool IsK8SEEnvironment()
        {
            return !String.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(Constants.KubernetesBuildService));
        }

        public static string GetLinuxFxVersion(string appName)
        {
            K8SEApp app = GetK8SEApp(appName);
            return $"{app.Spec.codeAppSpec.Framework}|{app.Spec.codeAppSpec.FrameworkVersion}";
        }

        public static bool UpdateBuildNumber(string appName, string buildNumber)
        {
            try
            {

                K8SEApp app = new K8SEApp()
                {
                    ApiVersion = Constants.KubernetesAppServiceApiVersion,
                    Spec = new AppSpec()
                };

                app.Spec.codeAppSpec = new CodeAppSpec();

                app.Spec.codeAppSpec.BuildVersion = buildNumber;

                _k8seClient.PatchNamespacedCustomObject(
                    app,
                    Constants.KubernetesAppServiceApiGroup,
                    Constants.KubernetesAppServiceApiVersion,
                    Constants.KubernetesAppServiceAppNamespace,
                    Constants.KubernetesAppServiceAppPlural,
                    appName);
                return true;
            }
            catch (HttpOperationException ex)
            {
                if ((ex.Response != null)
                    && (ex.Response.StatusCode == HttpStatusCode.NotFound))
                {
                    return false;
                }

            }
            return false;
        }

        public static string GetAppName(HttpContext context)
        {
            var host = context.Request.Headers["Host"].ToString();
            var appName = "";

            if (host.IndexOf(".") <= 0)
            {
                context.Response.StatusCode = 401;
                // K8SE TODO: move this to resource map
                throw new InvalidOperationException("Couldn't recognize AppName");
            }
            else
            {
                appName = host.Substring(0, host.IndexOf("."));
            }

            try
            {
                // block any internal service communication to build server
                // K8SE TODO: Check source IP to be in the internal service range and block them
                Int32.Parse(appName);
                context.Response.StatusCode = 401;
                // K8SE TODO: move this to resource map
                throw new InvalidOperationException("Internal services prohibited to communicate with the build server.");
            }
            catch(Exception)
            {
                // pass
            }

            return appName;
        }

        public static K8SEApp GetK8SEApp(string siteName)
        {
            //Get the app
            return ExecuteAsyncKubernetesTask<K8SEApp>((task) => GetAppFromCluster(siteName));
        }

        private static K8SEApp GetAppFromCluster(string appName)
        {
            try
            {
                var resultFromCluster = _k8seClient.GetNamespacedCustomObject(
                                            Constants.KubernetesAppServiceApiGroup,
                                            Constants.KubernetesAppServiceApiVersion,
                                            Constants.KubernetesAppServiceAppNamespace,
                                            Constants.KubernetesAppServiceAppPlural,
                                            appName);

                if (resultFromCluster == null)
                {
                    return null;
                }

                K8SEApp castedAksWebApp = ((Newtonsoft.Json.Linq.JObject)resultFromCluster).ToObject<K8SEApp>();

                return castedAksWebApp;
            }
            catch (HttpOperationException ex)
            {
                if ((ex.Response != null)
                    && (ex.Response.StatusCode == HttpStatusCode.NotFound))
                {
                    return null;
                }

            }

            return null;
        }

        // Helper method to ensure that we get a different thread by avoiding use of ThreadPool's threads
        // when calling Kubernetes client async methods.
        private static T ExecuteAsyncKubernetesTask<T>(Func<object, T> action)
        {
            object result = Task.Factory.StartNew<T>(action, null, TaskCreationOptions.LongRunning).Result;

            return (T)result;
        }
    }
}
