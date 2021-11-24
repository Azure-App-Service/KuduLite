using k8s;
using k8s.Models;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Core.Kube
{
    public class BuildJobHelper
    {
        private const string BuildServiceContainerName = "build-service";
        public static async Task<int> RunWithBuildJob(
            string appRoot,
            IEnvironment env,
            string buildType,
            TraceLevel level,
            ITracer tracer,
            string zipFilePath = null)
        {
            var skipSslValidation = System.Environment.GetEnvironmentVariable(SettingsKeys.SkipSslValidation);
            tracer.Trace($"skipSslValidation: {skipSslValidation}");
            if (skipSslValidation == "1")
            {
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            }

            var step = tracer.Step(XmlTracer.StartBuildJobTrace, new Dictionary<string, string>
            {
                { "type", "process" },
                { "path", "kudu.exe" },
                { "appRoot", appRoot},
                { "buildType", buildType},
                { "zipFilePath", zipFilePath},
            });

            var completeBuildFile = Path.Combine(env.DeploymentsPath, Constants.BuildCompleteFile);
            using (step)
            {
                try
                {
                    tracer.Trace($"Delete completeBuild file: {completeBuildFile}");
                    //Cleanup complete file every time a seperate build job start.
                    if (FileSystemHelpers.FileExists(completeBuildFile))
                    {                        
                        FileSystemHelpers.DeleteFile(completeBuildFile);
                    }

                    IKubernetes client = GetKubernetesClient();
                    var podNamespace = System.Environment.GetEnvironmentVariable(SettingsKeys.PodNamespace);
                    string buildJobNamePrefix = $"{env.GetNormalizedK8SEAppName()}-build-job-";

                    await DeleteBuildJobAsync(client, podNamespace, buildJobNamePrefix, tracer);

                    var job = await CreateBuildJob(client, podNamespace, buildJobNamePrefix, appRoot, env, buildType, level, tracer, zipFilePath);

                    //This will check if either the build job is complete or if the build complete file (which is used to sync between build service and build job) has been created.
                    DateTime nextJobCheckTime = DateTime.UtcNow.AddMinutes(1);
                    while (true)
                    {
                        if (DateTime.UtcNow > nextJobCheckTime)
                        {
                            var currentJob = client.ListNamespacedJob(podNamespace).Items.FirstOrDefault(it => it.Metadata.Name == job);
                            //If job has completed with failure, So there isn't any complete file. we need to finish the deployment and relesae the lock.
                            if (currentJob == null || CheckBuildJobCompleted(currentJob, tracer))
                            {
                                break;
                            }

                            nextJobCheckTime = nextJobCheckTime.AddMinutes(1);
                        }

                        if (FileSystemHelpers.FileExists(completeBuildFile))
                        {
                            break;
                        }

                        await Task.Delay(5 * 1000);
                    }

                    tracer.Trace($"build job completed.");
                    FileSystemHelpers.DeleteFile(completeBuildFile);
                }
                catch (Exception e)
                {
                    tracer.TraceError("exception: {0}, stack: {1}", e.Message, e.StackTrace);
                    return 1;
                }
            }

            return 0;
        }

        public static bool CheckBuildJobCompleted(V1Job job, ITracer tracer)
        {
            int active = job.Status?.Active ?? 0;
            int succeeded = job.Status?.Succeeded ?? 0;
            int failed = job.Status?.Failed ?? 0;

            if (active == 0 && succeeded == 0 && failed == 0) {
                tracer.Trace($"{job.Metadata.Name} hasn't started yet");
                return false;
            }

            if (active > 0)
            {
                tracer.Trace($"{job.Metadata.Name} is still running");
                return false;
            }

            if (succeeded > 0)
            {
                tracer.Trace($"{job.Metadata.Name} is completed successfully");
                return true;
            }

            tracer.Trace($"{job.Metadata.Name} is failed");
            return true;
        }

        public static async Task DeleteBuildJobAsync(IKubernetes client, string podNamespace, string jobPrefix, ITracer tracer)
        {
            string jobNamePattern = @"^"+jobPrefix + @"[0-9A-Fa-f]{8}";
            using (tracer.Step("Delete build jobs and pods"))
            {
                try
                {
                    var jobs = await client.ListNamespacedJobAsync(podNamespace);
                    var deleteJobs = jobs.Items.Where(it => Regex.Match(it.Metadata.Name, jobNamePattern).Success && (!it.Status.Active.HasValue || it.Status.Active.Value == 0));
                    foreach (var job in deleteJobs)
                    {
                        tracer.Trace("Delete job: {0}", job.Metadata.Name);
                        await client.DeleteNamespacedJobAsync(job.Metadata.Name, podNamespace, propagationPolicy: "Background");
                    }

                    tracer.Trace("Delete build job Completed");
                }
                catch (Exception ex)
                {
                    tracer.TraceError(ex, "Delete build job failed.");
                }
            
                try
                {
                    var pods = await client.ListNamespacedPodAsync(podNamespace);
                    var deletePods = pods.Items.Where(it => Regex.Match(it.Metadata.Name, jobNamePattern).Success
                        && (it.Status.Phase.Equals("Succeeded", StringComparison.OrdinalIgnoreCase)
                            || it.Status.Phase.Equals("Failed", StringComparison.OrdinalIgnoreCase)));

                    foreach (var pod in deletePods)
                    {
                        tracer.Trace("Delete pod: {0}", pod.Metadata.Name);
                        await client.DeleteNamespacedPodAsync(pod.Metadata.Name, podNamespace);
                    }

                    tracer.Trace("Delete build pod Completed");
                }
                catch (Exception ex)
                {
                    tracer.TraceError(ex, "Delete build pod failed.");
                }
            }
        }

        public static void DeleteBuildJob(ITracer tracer)
        {
            IKubernetes client = GetKubernetesClient();

            var podNamespace = System.Environment.GetEnvironmentVariable(SettingsKeys.PodNamespace);
            var jobName = System.Environment.GetEnvironmentVariable(SettingsKeys.BuildJobName);

            using (tracer.Step("Delete build job"))
            {
                try
                {
                    client.DeleteNamespacedJob(jobName, podNamespace, propagationPolicy: "Background");

                    tracer.Step("Delete build job Completed");
                }
                catch (Exception ex)
                {
                    tracer.TraceError(ex, "Delete build job failed.");
                }
            }
        }

        private static async Task<string> CreateBuildJob(IKubernetes client,
            string podNamespace,
            string buildJobNamePrefix,
            string appRoot,
            IEnvironment env,
            string buildType,
            TraceLevel level,
            ITracer tracer,
            string zipFilePath = null)
        {
            using (tracer.Step("Create build job"))
            {
                var podDeploymentName = System.Environment.GetEnvironmentVariable(SettingsKeys.PodDeploymentName);
                var deployment = await client.ListNamespacedDeploymentAsync(podNamespace);
                var appDeployment = deployment.Items.FirstOrDefault(n => n.Metadata.Name == env.K8SEAppName);

                string hostName = new Uri(env.AppBaseUrlPrefix).Host;
                foreach (var container in appDeployment.Spec.Template.Spec.Containers)
                {
                    var scmHost = container.Env.FirstOrDefault(e => e.Name.Equals("SCM_WEBSITE_HOSTNAME"));
                    if (scmHost != null)
                    {
                        hostName = scmHost.Value;
                    }
                }
                tracer.Trace($"scm hostName: {hostName}");

                string repositoryUri = null;
                if (buildType.Equals("zip"))
                {
                    repositoryUri = string.Format("https://{0}/api/vfs/{1}", hostName, zipFilePath);
                }
                else if (buildType.Equals("git") && !string.IsNullOrEmpty(hostName))
                {
                    //todo: the last part will be changed after swap. Need to get the git path from the scm info.
                    //example: https://teststagingnode14-test.scm.howangarc-euap1--fujkh1k.centraluseuap.k4apps.io/api/vfs/
                    repositoryUri = string.Format("https://{0}/{1}.git", hostName, env.K8SEAppName);
                }
                tracer.Trace($"repositoryUri: {repositoryUri}");

                string buildJobNameSuffix = Guid.NewGuid().ToString()[..8];
                string buildJobName = $"{buildJobNamePrefix}{buildJobNameSuffix}";
                var buildJobConfigMap = System.Environment.GetEnvironmentVariable(SettingsKeys.BuildJobConfigMap);
                var buildJobImage = System.Environment.GetEnvironmentVariable(SettingsKeys.BuildJobImage);

                var job = await client.CreateNamespacedJobAsync(
                    new V1Job()
                    {
                        Metadata = new V1ObjectMeta { Name = buildJobName },
                        Spec = new V1JobSpec
                        {
                            Template = new V1PodTemplateSpec
                            {
                                Spec = new V1PodSpec
                                {
                                    RestartPolicy = "Never",
                                    Containers = new[]
                                    {
                                            new V1Container()
                                            {
                                                Name = $"{buildJobNamePrefix}container",
                                                Image = buildJobImage,
                                                Command = new List<string>() { "/bin/sh", "-c" },
                                                Args = new List<string>() { $"cd /opt/Kudu; mkdir -p {appRoot}; dotnet ./KuduConsole/kudu.dll {appRoot} {buildType} {repositoryUri}" },
                                                Env = new List<V1EnvVar>
                                                {
                                                    new V1EnvVar
                                                    {
                                                        Name = "SYSTEM_NAMESPACE",
                                                        ValueFrom = new V1EnvVarSource { FieldRef = new V1ObjectFieldSelector { ApiVersion = "v1", FieldPath = "metadata.namespace" } }
                                                    },
                                                    new V1EnvVar
                                                    {
                                                        Name = "POD_NAME",
                                                        ValueFrom = new V1EnvVarSource { FieldRef = new V1ObjectFieldSelector { ApiVersion = "v1", FieldPath = "metadata.name" } }
                                                    },
                                                    new V1EnvVar
                                                    {
                                                        Name = "POD_NAMESPACE",
                                                        ValueFrom = new V1EnvVarSource { FieldRef = new V1ObjectFieldSelector { ApiVersion = "v1", FieldPath = "metadata.namespace" } }
                                                    },
                                                    new V1EnvVar
                                                    {
                                                        Name = "JOB_NAME",
                                                        Value = buildJobName
                                                    },
                                                    new V1EnvVar
                                                    {
                                                        Name = Constants.HttpHost,
                                                        Value = hostName
                                                    }
                                                },
                                                EnvFrom = new List<V1EnvFromSource>
                                                {
                                                    new V1EnvFromSource() { ConfigMapRef = new V1ConfigMapEnvSource() { Name = buildJobConfigMap } },
                                                    new V1EnvFromSource() { SecretRef = new V1SecretEnvSource() { Name = $"{env.K8SEAppName}-secrets" } }
                                                },
                                                ImagePullPolicy = "Always"
                                            },
                                    },
                                    ServiceAccountName = podDeploymentName
                                },
                            }
                        }
                    }, podNamespace);

                return job.Metadata.Name;
            }
        }

        private static Kubernetes GetKubernetesClient()
        {
            var config = KubernetesClientConfiguration.BuildDefaultConfig();
            return new Kubernetes(config);
        }
    }
}
