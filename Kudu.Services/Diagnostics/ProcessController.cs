using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using k8s;
using Kudu.Core.K8SE;

namespace Kudu.Services.Diagnostics
{
    public class ProcessController : Controller
    {
        [HttpGet]
        public IActionResult GetAllProcesses()
        {
            var config = KubernetesClientConfiguration.BuildDefaultConfig();
            IKubernetes client = new Kubernetes(config);

            var appNamespace = K8SEDeploymentHelper.GetAppNamespace(HttpContext);
            var appName = K8SEDeploymentHelper.GetAppName(HttpContext);

            Console.WriteLine("===" + appNamespace + "===");
            Console.WriteLine("===" + appName + "===");

            if (appNamespace == "")
            {
                appNamespace = "appservice-ns";
            }

            if (appName == "")
            {
                appName = "cz-as";
            }

            var a = new AppsModel();
            a.Name = appName;
            a.NamespaceName = appNamespace;

            var podList = K8SEDeploymentHelper.ListPodsForDeployment(client, appNamespace, appName);
            a.InstanceCount = podList.Items.Count;

            var podNameList = new List<string>();
            foreach (var item in podList.Items) {
                podNameList.Add(item.Metadata.Name);
            }
            a.PodNameList = podNameList;

            var cmd = "ls";
            var cmdQuery = HttpContext.Request.Query["cmd"];
            if (cmdQuery.Count != 0)
            {
                cmd = cmdQuery[0];
            }
            string str = K8SEDeploymentHelper.ExecInPod(client, appNamespace, podList.Items[0].Metadata.Name, cmd).Result;
            return new JsonResult(str);
        }
    }

    class AppsModel
    {
        public string NamespaceName { get; set; }
        public string Name { get; set; }
        public int InstanceCount { get; set; }
        public List<string> PodNameList { get; set; }
    }
}
