using System.IO;
using System.IO.Compression;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Settings;
using Kudu.Contracts.SourceControl;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Hooks;
using Kudu.Core.Settings;
using Kudu.Core.SourceControl.Git;
using Kudu.Core.Tracing;
using Kudu.Services.Performance;
using Kudu.Services.ServiceHookHandlers;
using Kudu.Services.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
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
            IEnvironment environment, 
            ITraceFactory traceFactory)
        {
            var logStreamManagerLock = KuduWebUtil.GetNamedLocks(traceFactory, environment)[Constants.HooksLockName];

            services.AddTransient(sp => new LogStreamManager(Path.Combine(environment.RootPath, Constants.LogFilesPath),
                sp.GetRequiredService<IEnvironment>(),
                sp.GetRequiredService<IDeploymentSettingsManager>(),
                sp.GetRequiredService<ITracer>(),
                logStreamManagerLock));
        }
        
        internal static void AddGitServer(this IServiceCollection services, IOperationLock deploymentLock)
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
            services.AddScoped<ISettings>(sp => new XmlSettings.Settings(KuduWebUtil.GetSettingsPath(environment)));
            services.AddScoped<IDeploymentSettingsManager, DeploymentSettingsManager>();
            services.AddScoped<IDeploymentStatusManager, DeploymentStatusManager>();
            services.AddScoped<ISiteBuilderFactory, SiteBuilderFactory>();
            services.AddScoped<IWebHooksManager, WebHooksManager>();
        }
    }
}