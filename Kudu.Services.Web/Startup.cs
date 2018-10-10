using System;
using System.Collections.Generic;
using System.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing.Constraints;
using Kudu.Contracts.Settings;
using Kudu.Core.Settings;
using Kudu.Core;
using System.IO;
using System.Net.Http.Formatting;
using Microsoft.AspNetCore.Http;
using Kudu.Core.Helpers;
using Kudu.Core.Infrastructure;
using Kudu.Contracts.Infrastructure;
using Kudu.Core.Tracing;
using Kudu.Contracts.Tracing;
using Kudu.Services.Web.Infrastructure;
using Kudu.Core.Deployment;
using Kudu.Contracts.SourceControl;
using Kudu.Core.SourceControl;
using Kudu.Services.GitServer;
using Kudu.Core.Commands;
using Newtonsoft.Json.Serialization;
using Kudu.Services.Web.Tracing;
using Kudu.Core.SSHKey;
using Kudu.Services.Diagnostics;
using Kudu.Services.Performance;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.Azure.Web.DataProtection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Targets;
using ILogger = Kudu.Core.Deployment.ILogger;

namespace Kudu.Services.Web
{
    public class Startup
    {
        private static string Format = "hh.mm.ss.ffffff";
        private readonly IHostingEnvironment _hostingEnvironment;
        private IEnvironment _webAppEnvironment;
        private IHttpContextAccessor _httpContextAccessor;
        private static readonly ServerConfiguration serverConfiguration = new ServerConfiguration();

        public Startup(IConfiguration configuration, IHostingEnvironment hostingEnvironment)
        {
            Console.WriteLine("\nStartup : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));
            Configuration = configuration;
            this._hostingEnvironment = hostingEnvironment;
        }

        private IConfiguration Configuration { get; }


        /// <summary>
        /// This method gets called by the runtime. It is used to add services 
        /// to the container. It uses the Extension pattern.
        /// </summary>
        /// <todo>
        ///   CORE TODO Remove initializing contextAccessor : This is new. See if over time we can refactor away the need for this?
        ///   It's kind of a quick hack/compatibility shim. Ideally you want to get the request context only from where
        ///   it's specifically provided to you (Request.HttpContext in a controller, or as an Invoke() parameter in
        ///   a middleware) and pass it wherever its needed.
        /// </todo>
        public void ConfigureServices(IServiceCollection services)
        {

            Console.WriteLine("\nConfigure Services : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));
            
            services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 52428800;
                options.ValueCountLimit = 500000;
                options.KeyLengthLimit = 500000;
            });


            services.AddMvcCore()
                .AddRazorPages()
                .AddAuthorization()
                .AddFormatterMappings()
                .AddJsonFormatters()
                .AddJsonOptions(options => options.SerializerSettings.ContractResolver = new DefaultContractResolver());
            

            services.AddGZipCompression();

            services.AddDirectoryBrowser();

            services.AddDataProtection();

            var contextAccessor = new HttpContextAccessor();
            this._httpContextAccessor = contextAccessor;
            services.AddSingleton<IHttpContextAccessor>(contextAccessor);

            KuduWebUtil.EnsureHomeEnvironmentVariable();

            KuduWebUtil.EnsureSiteBitnessEnvironmentVariable();

            IEnvironment environment = KuduWebUtil.GetEnvironment(_hostingEnvironment);

            _webAppEnvironment = environment;

            KuduWebUtil.EnsureDotNetCoreEnvironmentVariable(environment);

            // Add various folders that never change to the process path. All child processes will inherit this
            KuduWebUtil.PrependFoldersToPath(environment);

            // General
            services.AddSingleton<IServerConfiguration>(serverConfiguration);

            // CORE TODO Looks like this doesn't ever actually do anything, can refactor out?
            services.AddSingleton<IBuildPropertyProvider>(new BuildPropertyProvider());


            IDeploymentSettingsManager noContextDeploymentsSettingsManager =
                new DeploymentSettingsManager(new XmlSettings.Settings(KuduWebUtil.GetSettingsPath(environment)));
            TraceServices.TraceLevel = noContextDeploymentsSettingsManager.GetTraceLevel();

            // Per request environment
            services.AddScoped<IEnvironment>(sp => KuduWebUtil.GetEnvironment(_hostingEnvironment, sp.GetRequiredService<IDeploymentSettingsManager>(),
                sp.GetRequiredService<IHttpContextAccessor>().HttpContext));

            services.AddDeployementServices(environment);
            
            
            /*
             * CORE TODO all this business around ITracerFactory/ITracer/GetTracer()/
             * ILogger needs serious refactoring:
             * - Names should be changed to make it clearer that ILogger is for deployment
             * logging and ITracer and friends are for Kudu tracing
             * - ILogger is a first-class citizen in .NET core and has it's own meaning. We should be using it
             *   where appropriate (and not name-colliding with it)
             * - ITracer vs. ITraceFactory is redundant and confusing.
             * - All this stuff with funcs and factories and TraceServices is overcomplicated.
             * TraceServices only serves to confuse stuff now that we're avoiding
             */
            Func<IServiceProvider, ITracer> resolveTracer = sp => KuduWebUtil.GetTracer(sp);
            ITracer CreateTracerThunk() => resolveTracer(services.BuildServiceProvider());

            // First try to use the current request profiler if any, otherwise create a new one
            var traceFactory = new TracerFactory(() => {
                var sp = services.BuildServiceProvider();
                var context = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
                return TraceServices.GetRequestTracer(context) ?? resolveTracer(sp);
            });

            services.AddScoped<ITracer>(sp =>
            {
                var context = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
                return TraceServices.GetRequestTracer(context) ?? NullTracer.Instance;
            });

            services.AddSingleton<ITraceFactory>(traceFactory);

            TraceServices.SetTraceFactory(CreateTracerThunk);

            services.AddSingleton<IDictionary<string, IOperationLock>>(KuduWebUtil.GetNamedLocks(traceFactory,environment));

            // CORE TODO ShutdownDetector, used by LogStreamManager.
            //var shutdownDetector = new ShutdownDetector();
            //shutdownDetector.Initialize()

            var noContextTraceFactory = new TracerFactory(() => KuduWebUtil.GetTracerWithoutContext(environment, noContextDeploymentsSettingsManager));
            var etwTraceFactory = new TracerFactory(() => new ETWTracer(string.Empty, string.Empty));

            services.AddTransient<IAnalytics>(sp => new Analytics(sp.GetRequiredService<IDeploymentSettingsManager>(),
                                                                  sp.GetRequiredService<IServerConfiguration>(),
                                                                  noContextTraceFactory));
            
            // CORE TODO Trace unhandled exceptions
            //AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            //{
            //    var ex = args.ExceptionObject as Exception;
            //    if (ex != null)
            //    {
            //        kernel.Get<IAnalytics>().UnexpectedException(ex);
            //    }
            //};

            // CORE TODO
            // Trace shutdown event
            // Cannot use shutdownDetector.Token.Register because of race condition
            // with NinjectServices.Stop via WebActivator.ApplicationShutdownMethodAttribute
            //Shutdown += () => TraceShutdown(environment, noContextDeploymentsSettingsManager);

            // CORE TODO
            // LogStream service
            // The hooks and log stream start endpoint are low traffic end-points. Re-using it to avoid creating another lock
            var logStreamManagerLock = KuduWebUtil.GetNamedLocks(traceFactory, environment)["hooks"];
            //kernel.Bind<LogStreamManager>().ToMethod(context => new LogStreamManager(Path.Combine(environment.RootPath, Constants.LogFilesPath),
            //                                                                         context.Kernel.Get<IEnvironment>(),
            //                                                                         context.Kernel.Get<IDeploymentSettingsManager>(),
            //                                                                         context.Kernel.Get<ITracer>(),
            //                                                                         shutdownDetector,
            //                                                                         logStreamManagerLock));

            services.AddTransient(sp => new LogStreamManager(Path.Combine(environment.RootPath, Constants.LogFilesPath),
                                                             sp.GetRequiredService<IEnvironment>(),
                                                             sp.GetRequiredService<IDeploymentSettingsManager>(),
                                                             sp.GetRequiredService<ITracer>(),
                                                             logStreamManagerLock));

            // CORE TODO Need to implement this, and same comment as in InfoRefsController.cs (not sure why it needs the kernel/iserviceprovider as a
            // service locator, why does it need "delayed binding"?)
            //kernel.Bind<CustomGitRepositoryHandler>().ToMethod(context => new CustomGitRepositoryHandler(t => context.Kernel.Get(t)))
            //                                         .InRequestScope();

            // Deployment Service

            services.AddWebJobsDependencies();

            services.AddScoped<ILogger>(sp => KuduWebUtil.GetLogger(sp));

            services.AddScoped<IDeploymentManager, DeploymentManager>();
            services.AddScoped<IFetchDeploymentManager, FetchDeploymentManager>();
            services.AddScoped<ISSHKeyManager, SSHKeyManager>();

            services.AddScoped<IRepositoryFactory>(sp => KuduWebUtil.GetDeploymentLock(traceFactory,environment).RepositoryFactory = new RepositoryFactory(
                sp.GetRequiredService<IEnvironment>(), sp.GetRequiredService<IDeploymentSettingsManager>(), sp.GetRequiredService<ITraceFactory>()));

            // CORE NOTE This was previously wired up in Ninject with .InSingletonScope. I'm not sure how that worked,
            // since it depends on an IEnvironment, which was set up with .PerRequestScope. I have made this per request.
            services.AddScoped<IApplicationLogsReader, ApplicationLogsReader>();

            // Git server
            services.AddGitServer(KuduWebUtil.GetDeploymentLock(traceFactory,environment));

            // Git Servicehook Parsers
            services.AddGitServiceHookParsers();

            // CORE TODO
            // SiteExtensions
            //kernel.Bind<ISiteExtensionManager>().To<SiteExtensionManager>().InRequestScope();

            // CORE TODO
            // Functions
            //kernel.Bind<IFunctionManager>().To<FunctionManager>().InRequestScope();

            services.AddScoped<ICommandExecutor, CommandExecutor>();

            // CORE TODO This stuff should probably go in a separate method
            // (they don't really fit into "ConfigureServices"), and much of it is probably no longer needed
            //MigrateSite(environment, noContextDeploymentsSettingsManager);
            //RemoveOldTracePath(environment);
            //RemoveTempFileFromUserDrive(environment);

            //// Temporary fix for https://github.com/npm/npm/issues/5905
            //EnsureNpmGlobalDirectory();
            //EnsureUserProfileDirectory();

            //// Skip SSL Certificate Validate
            //if (Kudu.Core.Environment.SkipSslValidation)
            //{
            //     ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            //}

            //// Make sure webpages:Enabled is true. Even though we set it in web.config, it could be overwritten by
            //// an Azure AppSetting that's supposed to be for the site only but incidently affects Kudu as well.
            ConfigurationManager.AppSettings["webpages:Enabled"] = "true";

            //// Kudu does not rely owin:appStartup.  This is to avoid Azure AppSetting if set.
            if (ConfigurationManager.AppSettings?["owin:appStartup"] != null)
            {
                // Set the appSetting to null since we cannot use AppSettings.Remove(key) (ReadOnly exception!)
                ConfigurationManager.AppSettings["owin:appStartup"] = null;
            }

            //RegisterRoutes(kernel, RouteTable.Routes);

            //// Register the default hubs route: ~/signalr
            //GlobalHost.DependencyResolver = new SignalRNinjectDependencyResolver(kernel);
            //GlobalConfiguration.Configuration.Filters.Add(
            //    new TraceDeprecatedActionAttribute(
            //        kernel.Get<IAnalytics>(),
            //        kernel.Get<ITraceFactory>()));
            //GlobalConfiguration.Configuration.Filters.Add(new EnsureRequestIdHandlerAttribute());
            
            //FileTarget target = LogManager.Configuration.FindTargetByName("file") as FileTarget;
            //String logfile = _webAppEnvironment.LogFilesPath + "/.txt";
            //target.FileName = logfile;
        }

        // CORE TODO See NinjectServices.Stop

        // CORE TODO See signalr stuff in NinjectServices

        private static Uri GetAbsoluteUri(HttpContext httpContext)
        {
            var request = httpContext.Request;
            UriBuilder uriBuilder = new UriBuilder();
            uriBuilder.Scheme = request.Scheme;
            uriBuilder.Host = request.Host.Host;
            uriBuilder.Path = request.Path.ToString();
            uriBuilder.Query = request.QueryString.ToString();
            return uriBuilder.Uri;
        }

        public void Configure(IApplicationBuilder app, 
            IApplicationLifetime applicationLifetime,
            ILoggerFactory loggerFactory)
        {
            Console.WriteLine("\nConfigure : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));

            loggerFactory.AddEventSourceLogger();
            
            
            if (_hostingEnvironment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                //app.UseBrowserLink();
            }
            else
            {
                // CORE TODO
                app.UseExceptionHandler("/Error");
            }
            
            var containsRelativePath = new Func<HttpContext, bool>(i =>
                i.Request.Path.Value.StartsWith("/Default", StringComparison.OrdinalIgnoreCase));
            
            app.MapWhen(containsRelativePath, application => application.Run(async context =>
            {
                //context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync("Kestrel Running");
            }));
            
            var containsRelativePath2 = new Func<HttpContext, bool>(i =>
                i.Request.Path.Value.StartsWith("/Version", StringComparison.OrdinalIgnoreCase));
            
            app.MapWhen(containsRelativePath2, application => application.Run(async context =>
            {
                //context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsync("KuduL:2");
            }));
          
            
            app.UseResponseCompression();
            
            applicationLifetime.ApplicationStopping.Register(OnShutdown);
            
            app.UseStaticFiles();

            ProxyRequestsIfRelativeUrlMatch(@"/webssh", "http", "127.0.0.1", "3000", app);

            ProxyRequestsIfRelativeUrlMatch(@"/AppServiceTunnel/Tunnel.ashx", "http", "127.0.0.1", "5000", app);

            ProxyRequestsIfRelativeUrlMatch(@"/AppServiceTunnel/Tunnel.ashx", "http", "127.0.0.1", "5000", app);

            var configuration = app.ApplicationServices.GetRequiredService<IServerConfiguration>();

            // CORE TODO any equivalent for this? Needed?
            //var configuration = kernel.Get<IServerConfiguration>();
            //GlobalConfiguration.Configuration.Formatters.Clear();
            //GlobalConfiguration.Configuration.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;

            var jsonFormatter = new JsonMediaTypeFormatter();
            //GlobalConfiguration.Configuration.Formatters.Add(jsonFormatter);
            //GlobalConfiguration.Configuration.DependencyResolver = new NinjectWebApiDependencyResolver(kernel);
            //GlobalConfiguration.Configuration.Filters.Add(new TraceExceptionFilterAttribute());


            // CORE TODO concept of "deprecation" in routes for traces

            // Push url
            foreach (var url in new[] { "/git-receive-pack", $"/{configuration.GitServerRoot}/git-receive-pack" })
            {
                app.Map(url, appBranch => appBranch.RunReceivePackHandler());
            };

            // Fetch hook
            app.Map("/deploy", appBranch => appBranch.RunFetchHandler());

            // Log streaming
            app.Map("/api/logstream", appBranch => appBranch.RunLogStreamHandler());

            // Clone url
            foreach (var url in new[] { "/git-upload-pack", $"/{configuration.GitServerRoot}/git-upload-pack" })
            {
                app.Map(url, appBranch => appBranch.RunUploadPackHandler());
            };

            // CORE TODO
            // Custom GIT repositories, which can be served from any directory that has a git repo
            //routes.MapHandler<CustomGitRepositoryHandler>(kernel, "git-custom-repository", "git/{*path}", deprecated: false);

            // Custom GIT repositories, which can be served from any directory that has a git repo
            foreach (var url in new[] { "/git-custom-repository", "/git/{*path}" })
            {
                app.Map(url, appBranch => appBranch.RunCustomGitRepositoryHandler());
            };
            
            app.UseFileServer(new FileServerOptions
            {
                FileProvider = new PhysicalFileProvider(
                    _webAppEnvironment.WebRootPath),
                RequestPath = "/wwwroot",
                EnableDirectoryBrowsing = true
            });

            app.UseTraceMiddleware();
            
            //app.UseStaticFiles();
            app.UseMvc(routes =>
            {
                Console.WriteLine("\nSetting Up Routes : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));

                // CORE TODO Default route needed?
                routes.MapRoute(
                    name: "default",
                    template: "{controller}/{action=Index}/{id?}");

                // Git Service
                routes.MapRoute("git-info-refs-root", "info/refs", new { controller = "InfoRefs", action = "Execute" });
                routes.MapRoute("git-info-refs", configuration.GitServerRoot + "/info/refs", new { controller = "InfoRefs", action = "Execute" });

                // Scm (deployment repository)
                routes.MapHttpRouteDual("scm-info", "scm/info", new { controller = "LiveScm", action = "GetRepositoryInfo" });
                routes.MapHttpRouteDual("scm-clean", "scm/clean", new { controller = "LiveScm", action = "Clean" });
                routes.MapHttpRouteDual("scm-delete", "scm", new { controller = "LiveScm", action = "Delete" }, new { verb = new HttpMethodRouteConstraint("DELETE") });


                // Scm files editor
                routes.MapHttpRouteDual("scm-get-files", "scmvfs/{*path}", new { controller = "LiveScmEditor", action = "GetItem" }, new { verb = new HttpMethodRouteConstraint("GET", "HEAD") });
                routes.MapHttpRouteDual("scm-put-files", "scmvfs/{*path}", new { controller = "LiveScmEditor", action = "PutItem" }, new { verb = new HttpMethodRouteConstraint("PUT") });
                routes.MapHttpRouteDual("scm-delete-files", "scmvfs/{*path}", new { controller = "LiveScmEditor", action = "DeleteItem" }, new { verb = new HttpMethodRouteConstraint("DELETE") });

                // Live files editor
                routes.MapHttpRouteDual("vfs-get-files", "vfs/{*path}", new { controller = "Vfs", action = "GetItem" }, new { verb = new HttpMethodRouteConstraint("GET", "HEAD") });
                routes.MapHttpRouteDual("vfs-put-files", "vfs/{*path}", new { controller = "Vfs", action = "PutItem" }, new { verb = new HttpMethodRouteConstraint("PUT") });
                routes.MapHttpRouteDual("vfs-delete-files", "vfs/{*path}", new { controller = "Vfs", action = "DeleteItem" }, new { verb = new HttpMethodRouteConstraint("DELETE") });
                
                // Zip file handler
                routes.MapHttpRouteDual("zip-get-files", "zip/{*path}", new { controller = "Zip", action = "GetItem" }, new { verb = new HttpMethodRouteConstraint("GET", "HEAD") });
                routes.MapHttpRouteDual("zip-put-files", "zip/{*path}", new { controller = "Zip", action = "PutItem" }, new { verb = new HttpMethodRouteConstraint("PUT") });
                
                // Zip push deployment
                routes.MapRoute("zip-push-deploy", "api/zipdeploy", new { controller = "PushDeployment", action = "ZipPushDeploy" }, new { verb = new HttpMethodRouteConstraint("POST") });
                routes.MapRoute("zip-war-deploy", "api/wardeploy", new { controller = "PushDeployment", action = "WarPushDeploy" }, new { verb = new HttpMethodRouteConstraint("POST") });
                
                // Live Command Line
                routes.MapHttpRouteDual("execute-command", "command", new { controller = "Command", action = "ExecuteCommand" }, new { verb = new HttpMethodRouteConstraint("POST") });

                // Deployments
                routes.MapHttpRouteDual("all-deployments", "deployments", new { controller = "Deployment", action = "GetDeployResults" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpRouteDual("one-deployment-get", "deployments/{id}", new { controller = "Deployment", action = "GetResult" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpRouteDual("one-deployment-put", "deployments/{id?}", new { controller = "Deployment", action = "Deploy" }, new { verb = new HttpMethodRouteConstraint("PUT") });
                routes.MapHttpRouteDual("one-deployment-delete", "deployments/{id}", new { controller = "Deployment", action = "Delete" }, new { verb = new HttpMethodRouteConstraint("DELETE")});
                routes.MapHttpRouteDual("one-deployment-log", "deployments/{id}/log", new { controller = "Deployment", action = "GetLogEntry" });
                routes.MapHttpRouteDual("one-deployment-log-details", "deployments/{id}/log/{logId}", new { controller = "Deployment", action = "GetLogEntryDetails" });

                // Deployment script
                routes.MapRoute("get-deployment-script", "api/deploymentscript", new { controller = "Deployment", action = "GetDeploymentScript" }, new { verb = new HttpMethodRouteConstraint("GET") });

                // CORE TODO
                // SSHKey
                routes.MapHttpRouteDual("get-sshkey", "api/sshkey", new { controller = "SSHKey", action = "GetPublicKey" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpRouteDual("put-sshkey", "api/sshkey", new { controller = "SSHKey", action = "SetPrivateKey" }, new { verb = new HttpMethodRouteConstraint("PUT") });
                routes.MapHttpRouteDual("delete-sshkey", "api/sshkey", new { controller = "SSHKey", action = "DeleteKeyPair" }, new { verb = new HttpMethodRouteConstraint("DELETE") });

                // Environment
                routes.MapHttpRouteDual("get-env", "environment", new { controller = "Environment", action = "Get" }, new { verb = new HttpMethodRouteConstraint("GET") });

                // Settings
                routes.MapHttpRouteDual("set-setting", "settings", new { controller = "Settings", action = "Set" }, new { verb = new HttpMethodRouteConstraint("POST") });
                routes.MapHttpRouteDual("get-all-settings", "settings", new { controller = "Settings", action = "GetAll" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpRouteDual("get-setting", "settings/{key}", new { controller = "Settings", action = "Get" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpRouteDual("delete-setting", "settings/{key}", new { controller = "Settings", action = "Delete" }, new { verb = new HttpMethodRouteConstraint("DELETE") });

                // Diagnostics
                routes.MapHttpRouteDual("diagnostics", "dump", new { controller = "Diagnostics", action = "GetLog" });
                routes.MapHttpRouteDual("diagnostics-set-setting", "diagnostics/settings", new { controller = "Diagnostics", action = "Set" }, new { verb = new HttpMethodRouteConstraint("POST") });
                routes.MapHttpRouteDual("diagnostics-get-all-settings", "diagnostics/settings", new { controller = "Diagnostics", action = "GetAll" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpRouteDual("diagnostics-get-setting", "diagnostics/settings/{key}", new { controller = "Diagnostics", action = "Get" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpRouteDual("diagnostics-delete-setting", "diagnostics/settings/{key}", new { controller = "Diagnostics", action = "Delete" }, new { verb = new HttpMethodRouteConstraint("DELETE") });

                // CORE TODO
                // Logs
                foreach (var url in new[] { "/logstream", "/logstream/{*path}" })
                {
                    app.Map(url, appBranch => appBranch.RunLogStreamHandler());
                };
                //routes.MapHttpRouteDual<LogStreamHandler>(kernel, "logstream", "logstream/{*path}");
                routes.MapHttpRouteDual("recent-logs", "api/logs/recent", new { controller = "Diagnostics", action = "GetRecentLogs" }, new { verb = new HttpMethodRouteConstraint("GET") });

                if (!OSDetector.IsOnWindows())
                {
                    routes.MapRoute("current-docker-logs-zip", "api/logs/docker/zip", new { controller = "Diagnostics", action = "GetDockerLogsZip" }, new { verb = new HttpMethodRouteConstraint("GET") });
                    routes.MapRoute("current-docker-logs", "api/logs/docker", new { controller = "Diagnostics", action = "GetDockerLogs" }, new { verb = new HttpMethodRouteConstraint("GET") });
                }

                // CORE TODO
                var processControllerName = OSDetector.IsOnWindows() ? "Process" : "LinuxProcess";

                // Processes
                routes.MapHttpProcessesRoute("all-processes", "", new { controller = processControllerName, action = "GetAllProcesses" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpProcessesRoute("one-process-get", "/{id}", new { controller = processControllerName, action = "GetProcess" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpProcessesRoute("one-process-delete", "/{id}", new { controller = processControllerName, action = "KillProcess" }, new { verb = new HttpMethodRouteConstraint("DELETE") });
                routes.MapHttpProcessesRoute("one-process-dump", "/{id}/dump", new { controller = processControllerName, action = "MiniDump" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpProcessesRoute("start-process-profile", "/{id}/profile/start", new { controller = processControllerName, action = "StartProfileAsync" }, new { verb = new HttpMethodRouteConstraint("POST") });
                routes.MapHttpProcessesRoute("stop-process-profile", "/{id}/profile/stop", new { controller = processControllerName, action = "StopProfileAsync" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpProcessesRoute("all-threads", "/{id}/threads", new { controller = processControllerName, action = "GetAllThreads" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpProcessesRoute("one-process-thread", "/{processId}/threads/{threadId}", new { controller = processControllerName, action = "GetThread" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpProcessesRoute("all-modules", "/{id}/modules", new { controller = processControllerName, action = "GetAllModules" }, new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapHttpProcessesRoute("one-process-module", "/{id}/modules/{baseAddress}", new { controller = processControllerName, action = "GetModule" }, new { verb = new HttpMethodRouteConstraint("GET") });

                // Runtime
                routes.MapHttpRouteDual("runtime", "diagnostics/runtime", new { controller = "Runtime", action = "GetRuntimeVersions" }, new { verb = new HttpMethodRouteConstraint("GET") });

                // Hooks
                //routes.MapHttpRouteDual("unsubscribe-hook", "hooks/{id}", new { controller = "WebHooks", action = "Unsubscribe" }, new { verb = new HttpMethodConstraint("DELETE") });
                //routes.MapHttpRouteDual("get-hook", "hooks/{id}", new { controller = "WebHooks", action = "GetWebHook" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpRouteDual("publish-hooks", "hooks/publish/{hookEventType}", new { controller = "WebHooks", action = "PublishEvent" }, new { verb = new HttpMethodConstraint("POST") });
                //routes.MapHttpRouteDual("get-hooks", "hooks", new { controller = "WebHooks", action = "GetWebHooks" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpRouteDual("subscribe-hook", "hooks", new { controller = "WebHooks", action = "Subscribe" }, new { verb = new HttpMethodConstraint("POST") });

                // Jobs
                //routes.MapHttpWebJobsRoute("list-all-jobs", "", "", new { controller = "Jobs", action = "ListAllJobs" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpWebJobsRoute("list-triggered-jobs", "triggered", "", new { controller = "Jobs", action = "ListTriggeredJobs" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpWebJobsRoute("get-triggered-job", "triggered", "/{jobName}", new { controller = "Jobs", action = "GetTriggeredJob" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpWebJobsRoute("invoke-triggered-job", "triggered", "/{jobName}/run", new { controller = "Jobs", action = "InvokeTriggeredJob" }, new { verb = new HttpMethodConstraint("POST") });
                //routes.MapHttpWebJobsRoute("get-triggered-job-history", "triggered", "/{jobName}/history", new { controller = "Jobs", action = "GetTriggeredJobHistory" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpWebJobsRoute("get-triggered-job-run", "triggered", "/{jobName}/history/{runId}", new { controller = "Jobs", action = "GetTriggeredJobRun" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpWebJobsRoute("create-triggered-job", "triggered", "/{jobName}", new { controller = "Jobs", action = "CreateTriggeredJob" }, new { verb = new HttpMethodConstraint("PUT") });
                //routes.MapHttpWebJobsRoute("remove-triggered-job", "triggered", "/{jobName}", new { controller = "Jobs", action = "RemoveTriggeredJob" }, new { verb = new HttpMethodConstraint("DELETE") });
                //routes.MapHttpWebJobsRoute("get-triggered-job-settings", "triggered", "/{jobName}/settings", new { controller = "Jobs", action = "GetTriggeredJobSettings" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpWebJobsRoute("set-triggered-job-settings", "triggered", "/{jobName}/settings", new { controller = "Jobs", action = "SetTriggeredJobSettings" }, new { verb = new HttpMethodConstraint("PUT") });
                //routes.MapHttpWebJobsRoute("list-continuous-jobs", "continuous", "", new { controller = "Jobs", action = "ListContinuousJobs" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpWebJobsRoute("get-continuous-job", "continuous", "/{jobName}", new { controller = "Jobs", action = "GetContinuousJob" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpWebJobsRoute("disable-continuous-job", "continuous", "/{jobName}/stop", new { controller = "Jobs", action = "DisableContinuousJob" }, new { verb = new HttpMethodConstraint("POST") });
                //routes.MapHttpWebJobsRoute("enable-continuous-job", "continuous", "/{jobName}/start", new { controller = "Jobs", action = "EnableContinuousJob" }, new { verb = new HttpMethodConstraint("POST") });
                //routes.MapHttpWebJobsRoute("create-continuous-job", "continuous", "/{jobName}", new { controller = "Jobs", action = "CreateContinuousJob" }, new { verb = new HttpMethodConstraint("PUT") });
                //routes.MapHttpWebJobsRoute("remove-continuous-job", "continuous", "/{jobName}", new { controller = "Jobs", action = "RemoveContinuousJob" }, new { verb = new HttpMethodConstraint("DELETE") });
                //routes.MapHttpWebJobsRoute("get-continuous-job-settings", "continuous", "/{jobName}/settings", new { controller = "Jobs", action = "GetContinuousJobSettings" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpWebJobsRoute("set-continuous-job-settings", "continuous", "/{jobName}/settings", new { controller = "Jobs", action = "SetContinuousJobSettings" }, new { verb = new HttpMethodConstraint("PUT") });
                //routes.MapHttpWebJobsRoute("request-passthrough-continuous-job", "continuous", "/{jobName}/passthrough/{*path}", new { controller = "Jobs", action = "RequestPassthrough" }, new { verb = new HttpMethodConstraint("GET", "HEAD", "PUT", "POST", "DELETE", "PATCH") });

                // Web Jobs as microservice
                //routes.MapHttpRoute("list-triggered-jobs-swagger", "api/triggeredwebjobsswagger", new { controller = "Jobs", action = "ListTriggeredJobsInSwaggerFormat" }, new { verb = new HttpMethodConstraint("GET") });

                // SiteExtensions
                //routes.MapHttpRoute("api-get-remote-extensions", "api/extensionfeed", new { controller = "SiteExtension", action = "GetRemoteExtensions" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpRoute("api-get-remote-extension", "api/extensionfeed/{id}", new { controller = "SiteExtension", action = "GetRemoteExtension" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpRoute("api-get-local-extensions", "api/siteextensions", new { controller = "SiteExtension", action = "GetLocalExtensions" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpRoute("api-get-local-extension", "api/siteextensions/{id}", new { controller = "SiteExtension", action = "GetLocalExtension" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpRoute("api-uninstall-extension", "api/siteextensions/{id}", new { controller = "SiteExtension", action = "UninstallExtension" }, new { verb = new HttpMethodConstraint("DELETE") });
                //routes.MapHttpRoute("api-install-update-extension", "api/siteextensions/{id}", new { controller = "SiteExtension", action = "InstallExtension" }, new { verb = new HttpMethodConstraint("PUT") });

                // Functions
                //routes.MapHttpRoute("get-functions-host-settings", "api/functions/config", new { controller = "Function", action = "GetHostSettings" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpRoute("put-functions-host-settings", "api/functions/config", new { controller = "Function", action = "PutHostSettings" }, new { verb = new HttpMethodConstraint("PUT") });
                //routes.MapHttpRoute("api-sync-functions", "api/functions/synctriggers", new { controller = "Function", action = "SyncTriggers" }, new { verb = new HttpMethodConstraint("POST") });
                // This route only needed for temporary workaround. Will yank when /syncfunctionapptriggers is supported in ARM
                //routes.MapHttpRoute("api-sync-functions-tmphack", "functions/listsynctriggers", new { controller = "Function", action = "SyncTriggers" }, new { verb = new HttpMethodConstraint("POST") });
                //routes.MapHttpRoute("put-function", "api/functions/{name}", new { controller = "Function", action = "CreateOrUpdate" }, new { verb = new HttpMethodConstraint("PUT") });
                //routes.MapHttpRoute("list-functions", "api/functions", new { controller = "Function", action = "List" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpRoute("get-function", "api/functions/{name}", new { controller = "Function", action = "Get" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpRoute("list-secrets", "api/functions/{name}/listsecrets", new { controller = "Function", action = "GetSecrets" }, new { verb = new HttpMethodConstraint("POST") });
                //routes.MapHttpRoute("get-masterkey", "api/functions/admin/masterkey", new { controller = "Function", action = "GetMasterKey" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpRoute("get-admintoken", "api/functions/admin/token", new { controller = "Function", action = "GetAdminToken" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpRoute("delete-function", "api/functions/{name}", new { controller = "Function", action = "Delete" }, new { verb = new HttpMethodConstraint("DELETE") });
                //routes.MapHttpRoute("download-functions", "api/functions/admin/download", new { controller = "Function", action = "DownloadFunctions" }, new { verb = new HttpMethodConstraint("GET") });

                // Docker Hook Endpoint
                if (!OSDetector.IsOnWindows())
                {
                    routes.MapHttpRouteDual("docker", "docker/hook", new { controller = "Docker", action = "ReceiveHook" }, new { verb = new HttpMethodRouteConstraint("POST") });
                }
                
                // catch all unregistered url to properly handle not found
                // this is to work arounf the issue in TraceModule where we see double OnBeginRequest call
                // for the same request (404 and then 200 statusCode).
                routes.MapRoute("error-404", "{*path}", new { controller = "Error404", action = "Handle" });
            });

            // CORE TODO Remove This
            /*
            var containsRelativePath2 = new Func<HttpContext, bool>(i =>
                i.Request.Path.Value.StartsWith("/TestException", StringComparison.OrdinalIgnoreCase));
            
            app.MapWhen(containsRelativePath2, application => application.Run(async context =>
            {
                //context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                //await context.Response.WriteAsync("Kestrel Running");
                throw new Exception("Exception Handler Test");
            }));
            */
            /*
           app.Run(async context =>
            {
                await context.Response.WriteAsync("Krestel Running"); // returns a 200
            });
            */
            Console.WriteLine("\nExiting Configure : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));
        }

        // <summary>
        // 
        // </summary>
        // <param name="app"></param>
        // <param name="httpContext"></param>
        // <param name="relativeUrl"></param>
        // <param name="scheme"></param>
        // <param name="host"></param>
        // <param name="port"></param>
        private static void ProxyRequestsIfRelativeUrlMatch(
            string relativeUrl,
            string scheme,
            string host,
            string port,
            IApplicationBuilder app)
        {
            // Console.WriteLine("Forwarding request"+relativeUrl);
            var containsRelativePath = new Func<HttpContext, bool>(i =>
                i.Request.Path.Value.StartsWith(relativeUrl, StringComparison.OrdinalIgnoreCase));
            
            app.MapWhen(containsRelativePath, builder => builder.RunProxy(new ProxyOptions
            {
                    Scheme = scheme,
                    Host = host,
                    Port = port
            }));
        }

        
        private static HttpContext GetHttpContext(HttpContext httpContext) => httpContext;
        
        private static Func<HttpContext, bool> ContainsRelativeUrl(string relativeUrl, HttpContext httpContext)
        {
            return x => httpContext.Request.Path.Value.StartsWith(relativeUrl, StringComparison.OrdinalIgnoreCase);
        }
        
        private void OnShutdown()
        {
            //Wait while the data is flushed
            System.Threading.Thread.Sleep(1000);
        }
    }
}
