using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using k8s;
using Kudu.Contracts.Settings;
using Kudu.Contracts.SourceControl;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Hooks;
using Kudu.Core.K8SE;
using Kudu.Core.Settings;
using Kudu.Core.SourceControl.Git;
using Kudu.Core.Tracing;
using Kudu.Services.Performance;
using Kudu.Services.ServiceHookHandlers;
using Kudu.Services.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Rest;
using Microsoft.Rest.TransientFaultHandling;
using XmlSettings;

namespace Kudu.Services.Web
{
    public static class ServiceExtensions
    {
        internal static void AddGitServiceHookParsers(this IServiceCollection services)
        {
            services.AddScoped<IServiceHookHandler, GenericHandler>();
            services.AddScoped<IServiceHookHandler, GitHubHandler>();
            services.AddScoped<IServiceHookHandler, BitbucketHandler>();
            services.AddScoped<IServiceHookHandler, BitbucketHandlerV2>();
            services.AddScoped<IServiceHookHandler, DropboxHandler>();
            services.AddScoped<IServiceHookHandler, CodePlexHandler>();
            services.AddScoped<IServiceHookHandler, CodebaseHqHandler>();
            services.AddScoped<IServiceHookHandler, GitlabHqHandler>();
            services.AddScoped<IServiceHookHandler, GitHubCompatHandler>();
            services.AddScoped<IServiceHookHandler, KilnHgHandler>();
            services.AddScoped<IServiceHookHandler, VSOHandler>();
            services.AddScoped<IServiceHookHandler, OneDriveHandler>();
        }

        internal static void AddLogStreamService(this IServiceCollection services, 
            IEnvironment environment)
        {
            services.AddTransient(sp =>
            {
                var env = sp.GetEnvironment(environment);
                var traceFactory = sp.GetRequiredService<ITraceFactory>();
                var logStreamManagerLock = KuduWebUtil.GetNamedLocks(traceFactory, env)[Constants.HooksLockName];
                return new LogStreamManager(Path.Combine(environment.RootPath, Constants.LogFilesPath),
                    sp.GetRequiredService<IEnvironment>(),
                    sp.GetRequiredService<IDeploymentSettingsManager>(),
                    sp.GetRequiredService<ITracer>(),
                    logStreamManagerLock);
            });
        }
        
        internal static void AddGitServer(this IServiceCollection services, IEnvironment environment)
        {
            services.AddTransient<IDeploymentEnvironment, DeploymentEnvironment>();
            services.AddScoped<IGitServer>(sp =>
            {
                var env = sp.GetEnvironment(environment);
                var traceFactory = sp.GetRequiredService<ITraceFactory>();
                var deploymentLock = KuduWebUtil.GetDeploymentLock(traceFactory, env);
                return new GitExeServer(
                    sp.GetRequiredService<IEnvironment>(),
                    deploymentLock,
                    KuduWebUtil.GetRequestTraceFile(sp),
                    sp.GetRequiredService<IRepositoryFactory>(),
                    sp.GetRequiredService<IDeploymentEnvironment>(),
                    sp.GetRequiredService<IDeploymentSettingsManager>(),
                    traceFactory);
            });
        }

        internal static void AddGZipCompression(this IServiceCollection services)
        {
            services.Configure<GzipCompressionProviderOptions>(options =>
                options.Level = CompressionLevel.Optimal);
            services.AddResponseCompression();
        }

        internal static void AddWebJobsDependencies(this IServiceCollection services)
        {
            //IContinuousJobsManager continuousJobManager = new AggregateContinuousJobsManager(
            //    etwTraceFactory,
            //    kernel.Get<IEnvironment>(),
            //    kernel.Get<IDeploymentSettingsManager>(),
            //    kernel.Get<IAnalytics>());

            //OperationManager.SafeExecute(triggeredJobsManager.CleanupDeletedJobs);
            //OperationManager.SafeExecute(continuousJobManager.CleanupDeletedJobs);

            //kernel.Bind<IContinuousJobsManager>().ToConstant(continuousJobManager)
            //                     .InTransientScope();
        }

        internal static void AddDeploymentServices(this IServiceCollection services, IEnvironment environment)
        {
            var settings = new XmlSettings.Settings(KuduWebUtil.GetSettingsPath(environment));
            services.AddScoped<ISettings>(sp => new XmlSettings.Settings(KuduWebUtil.GetSettingsPath(environment)));
            services.AddScoped<IDeploymentSettingsManager, DeploymentSettingsManager>(sp =>
            {
                var manager = new DeploymentSettingsManager(sp.GetRequiredService<ISettings>());
                var env = sp.GetRequiredService<IEnvironment>();
                KuduWebUtil.UpdateEnvironmentBySettings(env, manager);
                return manager;
            });
            services.AddScoped<IDeploymentStatusManager, DeploymentStatusManager>();
            services.AddScoped<ISiteBuilderFactory, SiteBuilderFactory>();
            services.AddScoped<IWebHooksManager, WebHooksManager>();
        }

        internal static IEnvironment GetEnvironment(this IServiceProvider sp, IEnvironment environment)
        {
            if (K8SEDeploymentHelper.IsBuildJob() || K8SEDeploymentHelper.UseBuildJob())
            {
                return sp.GetRequiredService<IEnvironment>();
            }

            return environment;
        }

        internal static void AddKubernetesClientFactory(this IServiceCollection services)
        {
            Console.WriteLine("add client");
            var handler = new RetryDelegatingHandler();
            var retryPolicy = new RetryPolicy(
                new HttpTransientErrorDetectionStrategy(),
                3,
                TimeSpan.FromSeconds(3));
            //services.AddTransient(_ => handler);
            services.AddHttpClient("K8s")
                        .AddTypedClient<IKubernetes>((httpClient, serviceProvider) =>
                        {
                            var config = KubernetesClientConfiguration.BuildDefaultConfig();
                            var client = new Kubernetes(
                                config,
                                httpClient);

                            var m = client.HttpMessageHandlers?.OfType<RetryDelegatingHandler>();
                            var handlerNames = (m == null) ? "null" : m.Count() + " " + string.Join("", m.Select(m => m.GetType().FullName));
                            Console.WriteLine($"{handlerNames}");
                            client.SetRetryPolicy(retryPolicy);
                            return client;
                        })
                        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                        {
                            ServerCertificateCustomValidationCallback = KubernetesClientFactory.ServerCertificateValidationCallback,
                        });
            ;//.AddHttpMessageHandler(_ => handler);

            //services.AddTransient<RetryDelegatingHandler>(_ => handler);
        }

        internal static void AddKubernetesClientFactory2(this IServiceCollection services)
        {
            Console.WriteLine("add client");
            var handler = new RetryDelegatingHandler();
            handler.RetryPolicy = new RetryPolicy(
                new HttpTransientErrorDetectionStrategy(),
                3,
                TimeSpan.FromSeconds(3));
            //services.AddTransient(_ => handler);
            services.AddHttpClient("k8s", c => { })
                .ConfigureHttpMessageHandlerBuilder(builder =>
                {
                    builder.PrimaryHandler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = KubernetesClientFactory.ServerCertificateValidationCallback,
                    };
                }).AddHttpMessageHandler(_ => handler);
            services.AddTransient<RetryDelegatingHandler>();
            services.AddSingleton<IKubernetesClientFactory, KubernetesClientFactory>();
        }
    }
}