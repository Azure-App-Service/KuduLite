using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Kudu.Contracts.Settings;
using Kudu.Core.Settings;
using XmlSettings;
using Kudu.Core;
using Kudu.Contracts.Infrastructure;
using Kudu.Core.Tracing;
using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Hooks;
using Kudu.Contracts.SourceControl;
using Kudu.Services.ServiceHookHandlers;
using Kudu.Core.SourceControl.Git;
using Kudu.Services.Web.Services;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Azure.Web.DataProtection;

namespace Kudu.Services.Web
{ 
    public static class ServiceExtensions
    {
        private static IOperationLock _deploymentLock;

        public static void AddGitServiceHookParsers(this IServiceCollection services)
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
        
        public static void AddGitServer(this IServiceCollection services, IOperationLock deploymentLock)
        {
            services.AddTransient<IDeploymentEnvironment, DeploymentEnvironment>();
            services.AddScoped<IGitServer>(sp =>
                new GitExeServer(
                    sp.GetRequiredService<IEnvironment>(),
                    deploymentLock,
                    KuduWebUtil.GetRequestTraceFile(sp),
                    sp.GetRequiredService<IRepositoryFactory>(),
                    sp.GetRequiredService<IDeploymentEnvironment>(),
                    sp.GetRequiredService<IDeploymentSettingsManager>(),
                    sp.GetRequiredService<ITraceFactory>()));
        }

        public static void AddGZipCompression(this IServiceCollection services)
        {
            services.Configure<GzipCompressionProviderOptions>(options => options.Level = System.IO.Compression.CompressionLevel.Optimal);
            services.AddResponseCompression();
        }

        public static void AddWebJobsDependencies(this IServiceCollection services)
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

        public static void AddDeployementServices(this IServiceCollection services, IEnvironment environment)
        {
            services.AddScoped<ISettings>(sp => new XmlSettings.Settings(KuduWebUtil.GetSettingsPath(environment)));
            services.AddScoped<IDeploymentSettingsManager, DeploymentSettingsManager>();
            services.AddScoped<IDeploymentStatusManager, DeploymentStatusManager>();
            services.AddScoped<ISiteBuilderFactory, SiteBuilderFactory>();
            services.AddScoped<IWebHooksManager, WebHooksManager>();
        }
    }
}