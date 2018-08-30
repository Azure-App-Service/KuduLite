using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Kudu.Contracts.Settings;
using Kudu.Core.Settings;
using XmlSettings;
using Kudu.Core;
using System.IO;
using Microsoft.AspNetCore.Http;
using Kudu.Services.Infrastructure;
using Kudu.Core.Helpers;
using Kudu.Core.Infrastructure;
using Kudu.Contracts.Infrastructure;
using Kudu.Core.Tracing;
using Kudu.Contracts.Tracing;
using Kudu.Services.Web.Infrastructure;
using System.Diagnostics;
using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Hooks;
using Kudu.Contracts.SourceControl;
using Kudu.Core.SourceControl;
using Kudu.Services.ServiceHookHandlers;
using Microsoft.Extensions.PlatformAbstractions;
using Kudu.Core.SourceControl.Git;
using Kudu.Services.Web.Services;
using Kudu.Services.GitServer;
using Kudu.Core.Commands;
using Newtonsoft.Json.Serialization;
using Kudu.Services.Web.Tracing;
using Kudu.Core.SSHKey;
using Kudu.Services.Diagnostics;
using Kudu.Services.Performance;

namespace Kudu.Services.Web
{
    public class Startup
    {
        // CORE TODO this is a hack, esp. considering the hardcoded path separator, hardcoded "Debug\netcoreapp2.0", etc.
        // The idea is that we want Kudu.Services.Web to run kudu.dll from the directory it was built to when we are locally
        // debugging, but in release, we will put the published app into a dedicated directory.
        // Note that the paths are relative to the ApplicationBasePath (Kudu.Services.Web bin directory)
        public const string KuduConsoleFilename = "kudu.dll";
        public const string KuduConsoleRelativePath = "KuduConsole";
        public const string KuduConsoleDevRelativePath = @"..\..\..\..\Kudu.Console\bin\Debug\netcoreapp2.0";
        private const string Format = "hh.mm.ss.ffffff";
        private readonly IHostingEnvironment hostingEnvironment;

        public Startup(IConfiguration configuration, IHostingEnvironment hostingEnvironment)
        {

            Console.WriteLine("\nStartup : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));
            Configuration = configuration;
            this.hostingEnvironment = hostingEnvironment;
        }

        public IConfiguration Configuration { get; }

        // CORE TODO Is this still true?
        // Due to a bug in Ninject we can't use Dispose to clean up LockFile so we shut it down manually
        private static DeploymentLockFile _deploymentLock;

        // This method gets called by the runtime. Use this method to add services to the container.
        // CORE TODO trace exceptions here, see the catch in NinjectServices.Start()
        public void ConfigureServices(IServiceCollection services)
        {

            //CORE TODO Remove this
            Console.WriteLine("\nConfigure Services : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));
            /*
            services.AddMvc(opts=>
            {
                opts.Filters.Add(new AutoLogAttribute());
            })
            .AddJsonOptions(options => options.SerializerSettings.ContractResolver = new DefaultContractResolver());
            */

            
            services.AddMvc()
           .AddJsonOptions(options => options.SerializerSettings.ContractResolver = new DefaultContractResolver());
            var serverConfiguration = new ServerConfiguration();
            // CORE TODO This is new. See if over time we can refactor away the need for this?
            // It's kind of a quick hack/compatibility shim. Ideally you want to get the request context only from where
            // it's specifically provided to you (Request.HttpContext in a controller, or as an Invoke() parameter in
            // a middleware) and pass it wherever its needed.
            var contextAccessor = new HttpContextAccessor();
            services.AddSingleton<IHttpContextAccessor>(contextAccessor);

            // Make sure %HOME% is correctly set
            EnsureHomeEnvironmentVariable();

            // CORE TODO check on this
            EnsureSiteBitnessEnvironmentVariable();

            IEnvironment environment = GetEnvironment(hostingEnvironment);

            EnsureDotNetCoreEnvironmentVariable(environment);

            // Add various folders that never change to the process path. All child processes will inherit
            PrependFoldersToPath(environment);

            // Per request environment
            services.AddScoped<IEnvironment>(sp => GetEnvironment(hostingEnvironment, sp.GetRequiredService<IDeploymentSettingsManager>(),
                sp.GetRequiredService<IHttpContextAccessor>().HttpContext));

            // General
            services.AddSingleton<IServerConfiguration>(serverConfiguration);

            // CORE TODO Looks like this doesn't ever actually do anything, can refactor out?
            services.AddSingleton<IBuildPropertyProvider>(new BuildPropertyProvider());

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
             * HttpContext.Current
             */
            
            Func<IServiceProvider, ITracer> resolveTracer = sp => GetTracer(sp);
            Func<ITracer> createTracerThunk = () => resolveTracer(services.BuildServiceProvider());

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

            TraceServices.SetTraceFactory(createTracerThunk);
            // Setup the deployment lock
            string lockPath = Path.Combine(environment.SiteRootPath, Constants.LockPath);
            string deploymentLockPath = Path.Combine(lockPath, Constants.DeploymentLockFile);
            string statusLockPath = Path.Combine(lockPath, Constants.StatusLockFile);
            string sshKeyLockPath = Path.Combine(lockPath, Constants.SSHKeyLockFile);
            string hooksLockPath = Path.Combine(lockPath, Constants.HooksLockFile);

            _deploymentLock = new DeploymentLockFile(deploymentLockPath, traceFactory);
            _deploymentLock.InitializeAsyncLocks();

            var statusLock = new LockFile(statusLockPath, traceFactory);
            var sshKeyLock = new LockFile(sshKeyLockPath, traceFactory);
            var hooksLock = new LockFile(hooksLockPath, traceFactory);

            // CORE TODO This originally used Ninject's "WhenInjectedInto" for specific instances. IServiceCollection
            // doesn't support this concept, or anything similar like named instances. There are a few possibilities, but the hack
            // solution for now is just injecting a dictionary of locks and letting each dependent resolve the one it needs.
            var namedLocks = new Dictionary<string, IOperationLock>
            {
                { "status", statusLock }, // DeploymentStatusManager
                { "ssh", sshKeyLock }, // SSHKeyController
                { "hooks", hooksLock }, // WebHooksManager
                { "deployment", _deploymentLock } // DeploymentController, DeploymentManager, SettingsController, FetchDeploymentManager, LiveScmController, ReceivePackHandlerMiddleware
            };

            services.AddSingleton<IDictionary<string, IOperationLock>>(namedLocks);

            // CORE TODO ShutdownDetector, used by LogStreamManager.
            //var shutdownDetector = new ShutdownDetector();
            //shutdownDetector.Initialize();

            IDeploymentSettingsManager noContextDeploymentsSettingsManager =
                new DeploymentSettingsManager(new XmlSettings.Settings(GetSettingsPath(environment)));

            TraceServices.TraceLevel = noContextDeploymentsSettingsManager.GetTraceLevel();

            var noContextTraceFactory = new TracerFactory(() => GetTracerWithoutContext(environment, noContextDeploymentsSettingsManager));
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
            var logStreamManagerLock = hooksLock;
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
            services.AddScoped<ISettings>(sp => new XmlSettings.Settings(GetSettingsPath(environment)));

            services.AddScoped<IDeploymentSettingsManager, DeploymentSettingsManager>();

            services.AddScoped<IDeploymentStatusManager, DeploymentStatusManager>();

            services.AddScoped<ISiteBuilderFactory, SiteBuilderFactory>();

            services.AddScoped<IWebHooksManager, WebHooksManager>();

            // CORE TODO Webjobs dependencies
            //ITriggeredJobsManager triggeredJobsManager = new TriggeredJobsManager(
            //    etwTraceFactory,
            //    kernel.Get<IEnvironment>(),
            //    kernel.Get<IDeploymentSettingsManager>(),
            //    kernel.Get<IAnalytics>(),
            //    kernel.Get<IWebHooksManager>());
            //kernel.Bind<ITriggeredJobsManager>().ToConstant(triggeredJobsManager)
            //                                 .InTransientScope();

            //TriggeredJobsScheduler triggeredJobsScheduler = new TriggeredJobsScheduler(
            //    triggeredJobsManager,
            //    etwTraceFactory,
            //    environment,
            //    kernel.Get<IDeploymentSettingsManager>(),
            //    kernel.Get<IAnalytics>());
            //kernel.Bind<TriggeredJobsScheduler>().ToConstant(triggeredJobsScheduler)
            //                                 .InTransientScope();

            //IContinuousJobsManager continuousJobManager = new ContinuousJobsManager(
            //    etwTraceFactory,
            //    kernel.Get<IEnvironment>(),
            //    kernel.Get<IDeploymentSettingsManager>(),
            //    kernel.Get<IAnalytics>());

            //OperationManager.SafeExecute(triggeredJobsManager.CleanupDeletedJobs);
            //OperationManager.SafeExecute(continuousJobManager.CleanupDeletedJobs);

            //kernel.Bind<IContinuousJobsManager>().ToConstant(continuousJobManager)
            //                     .InTransientScope();

            services.AddScoped<ILogger>(sp => GetLogger(sp));

            services.AddScoped<IDeploymentManager, DeploymentManager>();
            services.AddScoped<IFetchDeploymentManager, FetchDeploymentManager>();
            services.AddScoped<ISSHKeyManager, SSHKeyManager>();

            services.AddScoped<IRepositoryFactory>(sp => _deploymentLock.RepositoryFactory = new RepositoryFactory(
                sp.GetRequiredService<IEnvironment>(), sp.GetRequiredService<IDeploymentSettingsManager>(), sp.GetRequiredService<ITraceFactory>()));

            // CORE NOTE This was previously wired up in Ninject with .InSingletonScope. I'm not sure how that worked,
            // since it depends on an IEnvironment, which was set up with .PerRequestScope. I have made this per request.
            services.AddScoped<IApplicationLogsReader, ApplicationLogsReader>();

            // Git server
            services.AddTransient<IDeploymentEnvironment, DeploymentEnvironment>();


            services.AddScoped<IGitServer>(sp =>
                new GitExeServer(
                    sp.GetRequiredService<IEnvironment>(),
                    _deploymentLock,
                    GetRequestTraceFile(sp),
                    sp.GetRequiredService<IRepositoryFactory>(),
                    sp.GetRequiredService<IDeploymentEnvironment>(),
                    sp.GetRequiredService<IDeploymentSettingsManager>(),
                    sp.GetRequiredService<ITraceFactory>()));

            // Git Servicehook Parsers
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
            //    ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            //}

            //// Make sure webpages:Enabled is true. Even though we set it in web.config, it could be overwritten by
            //// an Azure AppSetting that's supposed to be for the site only but incidently affects Kudu as well.
            //ConfigurationManager.AppSettings["webpages:Enabled"] = "true";

            //// Kudu does not rely owin:appStartup.  This is to avoid Azure AppSetting if set.
            //if (ConfigurationManager.AppSettings["owin:appStartup"] != null)
            //{
            //    // Set the appSetting to null since we cannot use AppSettings.Remove(key) (ReadOnly exception!)
            //    ConfigurationManager.AppSettings["owin:appStartup"] = null;
            //}

            //RegisterRoutes(kernel, RouteTable.Routes);

            //// Register the default hubs route: ~/signalr
            //GlobalHost.DependencyResolver = new SignalRNinjectDependencyResolver(kernel);
            //GlobalConfiguration.Configuration.Filters.Add(
            //    new TraceDeprecatedActionAttribute(
            //        kernel.Get<IAnalytics>(),
            //        kernel.Get<ITraceFactory>()));
            //GlobalConfiguration.Configuration.Filters.Add(new EnsureRequestIdHandlerAttribute());
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

        public void Configure(IApplicationBuilder app)
        {
            Console.WriteLine("\nConfigure : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));

            app.UseMiddleware<StackifyMiddleware.RequestTracerMiddleware>();

            app.UseStaticFiles();

            app.MapWhen(LogPathOnConsole, builder => builder.RunProxy(new ProxyOptions
            {
                Scheme = "http",
                Host = "localhost",
                Port = "1234"
            }));

            app.MapWhen(IsWebSSHPath, builder => builder.RunProxy(new ProxyOptions
            {
                Scheme = "http",
                Host = "localhost",
                Port = "3000"
            }));

            app.MapWhen(IsTunnelServerPath, builder => builder.RunProxy(new ProxyOptions
            {
                Scheme = "http",
                Host = "localhost",
                Port = "6000"
            }));

            app.MapWhen(IsJavaDebugPath, builder => builder.RunProxy(new ProxyOptions
            {
                Scheme = "http",
                Host = "localhost",
                Port = "6000"
            }));

            if (hostingEnvironment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
            }
            else
            {
                // CORE TODO
                app.UseExceptionHandler("/Error");
            }

            //app.UseTraceMiddleware();

            var configuration = app.ApplicationServices.GetRequiredService<IServerConfiguration>();

            // CORE TODO any equivalent for this? Needed?
            //var configuration = kernel.Get<IServerConfiguration>();
            //GlobalConfiguration.Configuration.Formatters.Clear();
            //GlobalConfiguration.Configuration.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;

            //var jsonFormatter = new JsonMediaTypeFormatter();
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
            // app.Map("/api/logstream", appBranch => appBranch.RunLogStreamHandler());

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
                Console.WriteLine("Done");
                Console.WriteLine(DateTime.Now.ToString("hh.mm.ss.ffffff"));

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
                //routes.MapHttpRouteDual("get-sshkey", "sshkey", new { controller = "SSHKey", action = "GetPublicKey" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpRouteDual("put-sshkey", "sshkey", new { controller = "SSHKey", action = "SetPrivateKey" }, new { verb = new HttpMethodConstraint("PUT") });
                //routes.MapHttpRouteDual("delete-sshkey", "sshkey", new { controller = "SSHKey", action = "DeleteKeyPair" }, new { verb = new HttpMethodConstraint("DELETE") });

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
                //routes.MapHandlerDual<LogStreamHandler>(kernel, "logstream", "logstream/{*path}");
                //routes.MapHttpRoute("recent-logs", "api/logs/recent", new { controller = "Diagnostics", action = "GetRecentLogs" }, new { verb = new HttpMethodConstraint("GET") });

                if (!OSDetector.IsOnWindows())
                {
                    routes.MapRoute("current-docker-logs-zip", "api/logs/docker/zip", new { controller = "Diagnostics", action = "GetDockerLogsZip" }, new { verb = new HttpMethodRouteConstraint("GET") });
                    routes.MapRoute("current-docker-logs", "api/logs/docker", new { controller = "Diagnostics", action = "GetDockerLogs" }, new { verb = new HttpMethodRouteConstraint("GET") });
                }

                // CORE TODO
                //var processControllerName = OSDetector.IsOnWindows() ? "Process" : "LinuxProcess";

                // Processes
                //routes.MapHttpProcessesRoute("all-processes", "", new { controller = processControllerName, action = "GetAllProcesses" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpProcessesRoute("one-process-get", "/{id}", new { controller = processControllerName, action = "GetProcess" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpProcessesRoute("one-process-delete", "/{id}", new { controller = processControllerName, action = "KillProcess" }, new { verb = new HttpMethodConstraint("DELETE") });
                //routes.MapHttpProcessesRoute("one-process-dump", "/{id}/dump", new { controller = processControllerName, action = "MiniDump" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpProcessesRoute("start-process-profile", "/{id}/profile/start", new { controller = processControllerName, action = "StartProfileAsync" }, new { verb = new HttpMethodConstraint("POST") });
                //routes.MapHttpProcessesRoute("stop-process-profile", "/{id}/profile/stop", new { controller = processControllerName, action = "StopProfileAsync" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpProcessesRoute("all-threads", "/{id}/threads", new { controller = processControllerName, action = "GetAllThreads" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpProcessesRoute("one-process-thread", "/{processId}/threads/{threadId}", new { controller = processControllerName, action = "GetThread" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpProcessesRoute("all-modules", "/{id}/modules", new { controller = processControllerName, action = "GetAllModules" }, new { verb = new HttpMethodConstraint("GET") });
                //routes.MapHttpProcessesRoute("one-process-module", "/{id}/modules/{baseAddress}", new { controller = processControllerName, action = "GetModule" }, new { verb = new HttpMethodConstraint("GET") });

                // Runtime
                //routes.MapHttpRouteDual("runtime", "diagnostics/runtime", new { controller = "Runtime", action = "GetRuntimeVersions" }, new { verb = new HttpMethodConstraint("GET") });

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
                //if (!OSDetector.IsOnWindows())
                //{
                //    routes.MapHttpRoute("docker", "docker/hook", new { controller = "Docker", action = "ReceiveHook" }, new { verb = new HttpMethodConstraint("POST") });
                //}

                // catch all unregistered url to properly handle not found
                // this is to work arounf the issue in TraceModule where we see double OnBeginRequest call
                // for the same request (404 and then 200 statusCode).
                routes.MapRoute("error-404", "{*path}", new { controller = "Error404", action = "Handle" });
            });

            // CORE TODO Remove This
           app.Run(async context =>
            {
                await context.Response.WriteAsync("Krestel Running"); // returns a 200
            });
            Console.WriteLine("\nExiting Configure : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));
        }

        private static string GetSettingsPath(IEnvironment environment)
        {
            return Path.Combine(environment.DeploymentsPath, Constants.DeploySettingsPath);
        }

        private static IEnvironment GetEnvironment(IHostingEnvironment hostingEnvironment, IDeploymentSettingsManager settings = null, HttpContext httpContext = null)
        {
            string root = PathResolver.ResolveRootPath();
            string siteRoot = Path.Combine(root, Constants.SiteFolder);
            string repositoryPath = Path.Combine(siteRoot, settings == null ? Constants.RepositoryPath : settings.GetRepositoryPath());
            // CORE TODO see if we can refactor out PlatformServices as high up as we can?
            string binPath = PlatformServices.Default.Application.ApplicationBasePath;
            string requestId = httpContext?.Request.GetRequestId();
            string siteRetrictedJwt = httpContext?.Request.GetSiteRetrictedJwt();

            string kuduConsoleFullPath;

            // CORE TODO Clean this up
            if (hostingEnvironment.IsDevelopment())
            {
                kuduConsoleFullPath = Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, KuduConsoleDevRelativePath, KuduConsoleFilename);
            }
            else
            {
                kuduConsoleFullPath = Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, KuduConsoleRelativePath, KuduConsoleFilename);
            }

            // CORE TODO Environment now requires an HttpContextAccessor, which I have set to null here
            return new Core.Environment(root, EnvironmentHelper.NormalizeBinPath(binPath), repositoryPath, requestId, siteRetrictedJwt, kuduConsoleFullPath, null);
        }

        private static void EnsureHomeEnvironmentVariable()
        {
            // CORE TODO Hard-coding this for now while exploring. Have a look at what
            // PlatformServices.Default and the injected IHostingEnvironment have at runtime.
            if (!Directory.Exists(System.Environment.ExpandEnvironmentVariables(@"%HOME%")))
            {
                if (OSDetector.IsOnWindows())
                {
                    System.Environment.SetEnvironmentVariable("HOME", @"G:\kudu-debug");
                }
                else
                {
                    System.Environment.SetEnvironmentVariable("HOME", "/home");
                }
            }

            /*
            // If MapPath("/_app") returns a valid folder, set %HOME% to that, regardless of
            // it current value. This is the non-Azure code path.
            string path = HostingEnvironment.MapPath(Constants.MappedSite);
            if (Directory.Exists(path))
            {
                path = Path.GetFullPath(path);
                System.Environment.SetEnvironmentVariable("HOME", path);
            }
            */
        }

        private static ITracer GetTracer(IServiceProvider serviceProvider)
        {
            IEnvironment environment = serviceProvider.GetRequiredService<IEnvironment>();
            TraceLevel level = serviceProvider.GetRequiredService<IDeploymentSettingsManager>().GetTraceLevel();
            var contextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
            var httpContext = contextAccessor.HttpContext;
            var requestTraceFile = TraceServices.GetRequestTraceFile(httpContext);
            if (level > TraceLevel.Off && requestTraceFile != null)
            {
                string textPath = Path.Combine(environment.TracePath, requestTraceFile);
                return new CascadeTracer(new XmlTracer(environment.TracePath, level), new TextTracer(textPath, level), new ETWTracer(environment.RequestId, TraceServices.GetHttpMethod(httpContext)));
            }

            return NullTracer.Instance;
        }

        private static ITracer GetTracerWithoutContext(IEnvironment environment, IDeploymentSettingsManager settings)
        {
            // when file system has issue, this can throw (environment.TracePath calls EnsureDirectory).
            // prefer no-op tracer over outage.  
            return OperationManager.SafeExecute(() =>
            {
                TraceLevel level = settings.GetTraceLevel();
                if (level > TraceLevel.Off)
                {
                    return new XmlTracer(environment.TracePath, level);
                }

                return NullTracer.Instance;
            }) ?? NullTracer.Instance;
        }

        private static ILogger GetLogger(IServiceProvider serviceProvider)
        {
            IEnvironment environment = serviceProvider.GetRequiredService<IEnvironment>();
            TraceLevel level = serviceProvider.GetRequiredService<IDeploymentSettingsManager>().GetTraceLevel();
            var contextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
            var httpContext = contextAccessor.HttpContext;
            var requestTraceFile = TraceServices.GetRequestTraceFile(httpContext);
            if (level > TraceLevel.Off && requestTraceFile != null)
            {
                string textPath = Path.Combine(environment.DeploymentTracePath, requestTraceFile);
                return new TextLogger(textPath);
            }

            return NullLogger.Instance;
        }

        private static void PrependFoldersToPath(IEnvironment environment)
        {
            List<string> folders = PathUtilityFactory.Instance.GetPathFolders(environment);

            string path = System.Environment.GetEnvironmentVariable("PATH");
            string additionalPaths = String.Join(Path.PathSeparator.ToString(), folders);

            // Make sure we haven't already added them. This can happen if the Kudu appdomain restart (since it's still same process)
            if (!path.Contains(additionalPaths))
            {
                path = additionalPaths + Path.PathSeparator + path;

                // PHP 7 was mistakenly added to the path unconditionally on Azure. To work around, if we detect
                // some PHP v5.x anywhere on the path, we yank the unwanted PHP 7
                // TODO: remove once the issue is fixed on Azure
                if (path.Contains(@"PHP\v5"))
                {
                    path = path.Replace(@"D:\Program Files (x86)\PHP\v7.0" + Path.PathSeparator, String.Empty);
                }

                System.Environment.SetEnvironmentVariable("PATH", path);
            }
        }

        private static void EnsureDotNetCoreEnvironmentVariable(IEnvironment environment)
        {
            // Skip this as it causes huge files to be downloaded to the temp folder
            SetEnvironmentVariableIfNotYetSet("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "true");

            // Don't download xml comments, as they're large and provide no benefits outside of a dev machine
            SetEnvironmentVariableIfNotYetSet("NUGET_XMLDOC_MODE", "skip");
        
            if (Core.Environment.IsAzureEnvironment())
            {
                // On Azure, restore nuget packages to d:\home\.nuget so they're persistent. It also helps
                // work around https://github.com/projectkudu/kudu/issues/2056.
                // Note that this only applies to project.json scenarios (not packages.config)
                SetEnvironmentVariableIfNotYetSet("NUGET_PACKAGES", Path.Combine(environment.RootPath, ".nuget"));

                // Set the telemetry environment variable
                SetEnvironmentVariableIfNotYetSet("DOTNET_CLI_TELEMETRY_PROFILE", "AzureKudu");
            }
            else
            {
                // Set it slightly differently if outside of Azure to differentiate
                SetEnvironmentVariableIfNotYetSet("DOTNET_CLI_TELEMETRY_PROFILE", "Kudu");
            }
        }

        private static void SetEnvironmentVariableIfNotYetSet(string name, string value)
        {
            if (System.Environment.GetEnvironmentVariable(name) == null)
            {
                System.Environment.SetEnvironmentVariable(name, value);
            }
        }

        private static string GetRequestTraceFile(IServiceProvider serviceProvider)
        {
            TraceLevel level = serviceProvider.GetRequiredService<IDeploymentSettingsManager>().GetTraceLevel();
            // CORE TODO Need TraceServices implementation
            //if (level > TraceLevel.Off)
            //{
            //    return TraceServices.CurrentRequestTraceFile;
            //}

            return null;
        }

        private static void EnsureSiteBitnessEnvironmentVariable()
        {
            SetEnvironmentVariableIfNotYetSet("SITE_BITNESS", System.Environment.Is64BitProcess ? Constants.X64Bit : Constants.X86Bit);
        }
        private static bool IsTunnelServerPath(HttpContext httpContext)
        {
            return httpContext.Request.Path.Value.StartsWith(@"/AppServiceTunnel/Tunnel.ashx", StringComparison.OrdinalIgnoreCase);
        }
        private static bool IsWebSSHPath(HttpContext httpContext)
        {
            return httpContext.Request.Path.Value.StartsWith(@"/webssh/", StringComparison.OrdinalIgnoreCase);
        }
        private static bool IsJavaDebugPath(HttpContext httpContext)
        {
            return httpContext.Request.Path.Value.StartsWith(@"/DebugSiteExtension/JavaDebugSiteExtension.ashx", StringComparison.OrdinalIgnoreCase);
        }
        private static bool LogPathOnConsole(HttpContext context)
        {
            Console.WriteLine($"path: {context.Request.Path}");
            return false;
        }
    }
}
