using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Reflection;
using AspNetCore.RouteAnalyzer;
using Kudu.Contracts;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Scan;
using Kudu.Contracts.Settings;
using Kudu.Contracts.SourceControl;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Commands;
using Kudu.Core.Deployment;
using Kudu.Core.Helpers;
using Kudu.Core.Infrastructure;
using Kudu.Core.LinuxConsumption;
using Kudu.Core.Scan;
using Kudu.Core.Settings;
using Kudu.Core.SourceControl;
using Kudu.Core.SSHKey;
using Kudu.Core.Tracing;
using Kudu.Services.Diagnostics;
using Kudu.Services.GitServer;
using Kudu.Services.Performance;
using Kudu.Services.TunnelServer;
using Kudu.Services.Web.Infrastructure;
using Kudu.Services.Web.Tracing;
using Kudu.Services.LinuxConsumptionInstanceAdmin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Serialization;
using Swashbuckle.AspNetCore.Swagger;
using ILogger = Kudu.Core.Deployment.ILogger;
using AspNetCore.Proxy;

namespace Kudu.Services.Web
{
    public class Startup
    {
        private readonly IHostingEnvironment _hostingEnvironment;
        private IEnvironment _webAppRuntimeEnvironment;
        private IDeploymentSettingsManager _noContextDeploymentsSettingsManager;

        public Startup(IConfiguration configuration, IHostingEnvironment hostingEnvironment)
        {
            Console.WriteLine(@"Startup : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));
            Configuration = configuration;
            _hostingEnvironment = hostingEnvironment;
        }

        private IConfiguration Configuration { get; }


        /// <summary>
        /// This method gets called by the runtime. It is used to add services 
        /// to the container. It uses the Extension pattern.
        /// </summary>
        /// <todo>
        ///   CORE TODO Remove initializing contextAccessor : See if over time we can refactor away the need for this?
        ///   It's kind of a quick hack/compatibility shim. Ideally you want to get the request context only from where
        ///   it's specifically provided to you (Request.HttpContext in a controller, or as an Invoke() parameter in
        ///   a middleware) and pass it wherever its needed.
        /// </todo>
        public void ConfigureServices(IServiceCollection services)
        {
            Console.WriteLine(@"Configure Services : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));
            FileSystemHelpers.DeleteDirectorySafe("/home/site/locks/deployment");
            services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 52428800;
                options.ValueCountLimit = 500000;
                options.KeyLengthLimit = 500000;
            });

            services.AddRouteAnalyzer();

            // Kudu.Services contains all the Controllers 
            var kuduServicesAssembly = Assembly.Load("Kudu.Services");

            services.AddMvcCore()
                .AddRazorPages()
                .AddAuthorization()
                .AddJsonFormatters()
                .AddJsonOptions(options => options.SerializerSettings.ContractResolver = new DefaultContractResolver())
                .AddApplicationPart(kuduServicesAssembly).AddControllersAsServices()
                .AddApiExplorer();

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Info {Title = "Kudu API Docs"});
                // Setting the comments path for the Swagger JSON and UI.
                var xmlFile = $"Kudu.Services.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });

            services.AddGZipCompression();

            services.AddDirectoryBrowser();

            services.AddDataProtection();

            services.AddLogging(
                builder =>
                {
                    builder.AddFilter("Microsoft", LogLevel.Warning)
                        .AddFilter("System", LogLevel.Warning)
                        .AddConsole();
                });

            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            services.TryAddSingleton<ISystemEnvironment>(SystemEnvironment.Instance);
            services.AddSingleton<ILinuxConsumptionEnvironment, LinuxConsumptionEnvironment>();
            services.AddSingleton<ILinuxConsumptionInstanceManager, LinuxConsumptionInstanceManager>();
            services.AddSingleton<IFileSystemPathProvider, FileSystemPathProvider>();
            services.AddSingleton<IStorageClient, StorageClient>();

            KuduWebUtil.EnsureHomeEnvironmentVariable();

            KuduWebUtil.EnsureSiteBitnessEnvironmentVariable();

            var fileSystemPathProvider = new FileSystemPathProvider(new MeshPersistentFileSystem(SystemEnvironment.Instance,
                new MeshServiceClient(SystemEnvironment.Instance, new HttpClient()), new StorageClient(SystemEnvironment.Instance)));
            IEnvironment environment = KuduWebUtil.GetEnvironment(_hostingEnvironment, fileSystemPathProvider);

            _webAppRuntimeEnvironment = environment;

            services.AddSingleton(_ => new HttpClient());
            services.AddSingleton<IMeshServiceClient>(s =>
            {
                if (environment.IsOnLinuxConsumption)
                {
                    var httpClient = s.GetService<HttpClient>();
                    var systemEnvironment = s.GetService<ISystemEnvironment>();
                    return new MeshServiceClient(systemEnvironment, httpClient);
                }
                else
                {
                    return new NullMeshServiceClient();
                }
            });
            services.AddSingleton<IMeshPersistentFileSystem>(s =>
            {
                if (environment.IsOnLinuxConsumption)
                {
                    var meshServiceClient = s.GetService<IMeshServiceClient>();
                    var storageClient = s.GetService<IStorageClient>();
                    var systemEnvironment = s.GetService<ISystemEnvironment>();
                    return new MeshPersistentFileSystem(systemEnvironment, meshServiceClient, storageClient);
                }
                else
                {
                    return new NullMeshPersistentFileSystem();
                }
            });

            KuduWebUtil.EnsureDotNetCoreEnvironmentVariable(environment);

            // CORE TODO Check this
            // fix up invalid /home/site/deployments/settings.xml
            KuduWebUtil.EnsureValidDeploymentXmlSettings(environment);

            // Add various folders that never change to the process path. All child processes will inherit this
            KuduWebUtil.PrependFoldersToPath(environment);

            // Add middleware for Linux Consumption authentication and authorization
            // when KuduLIte is running in service fabric mesh
            services.AddLinuxConsumptionAuthentication();
            services.AddLinuxConsumptionAuthorization(environment);

            // General
            services.AddSingleton<IServerConfiguration, ServerConfiguration>();

            // CORE TODO Looks like this doesn't ever actually do anything, can refactor out?
            services.AddSingleton<IBuildPropertyProvider>(new BuildPropertyProvider());

            _noContextDeploymentsSettingsManager =
                new DeploymentSettingsManager(new XmlSettings.Settings(KuduWebUtil.GetSettingsPath(environment)));
            TraceServices.TraceLevel = _noContextDeploymentsSettingsManager.GetTraceLevel();

            // Per request environment
            services.AddScoped(sp =>
                KuduWebUtil.GetEnvironment(_hostingEnvironment, sp.GetRequiredService <IFileSystemPathProvider>(), sp.GetRequiredService<IDeploymentSettingsManager>()));

            services.AddDeploymentServices(environment);

            /*
             * CORE TODO Refactor ITracerFactory/ITracer/GetTracer()/
             * ILogger needs serious refactoring:
             * - Names should be changed to make it clearer that ILogger is for deployment
             *   logging and ITracer and friends are for Kudu tracing
             * - ILogger is a first-class citizen in .NET core and has it's own meaning. We should be using it
             *   where appropriate (and not name-colliding with it)
             * - ITracer vs. ITraceFactory is redundant and confusing.
             * - TraceServices only serves to confuse stuff now that we're avoiding
             */
            Func<IServiceProvider, ITracer> resolveTracer = KuduWebUtil.GetTracer;
            ITracer CreateTracerThunk() => resolveTracer(services.BuildServiceProvider());

            // First try to use the current request profiler if any, otherwise create a new one
            var traceFactory = new TracerFactory(() =>
            {
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

            services.AddSingleton<IDictionary<string, IOperationLock>>(
                KuduWebUtil.GetNamedLocks(traceFactory, environment));

            // CORE TODO ShutdownDetector, used by LogStreamManager.
            //var shutdownDetector = new ShutdownDetector();
            //shutdownDetector.Initialize()

            var noContextTraceFactory = new TracerFactory(() =>
                KuduWebUtil.GetTracerWithoutContext(environment, _noContextDeploymentsSettingsManager));

            services.AddTransient<IAnalytics>(sp => new Analytics(sp.GetRequiredService<IDeploymentSettingsManager>(),
                sp.GetRequiredService<IServerConfiguration>(),
                noContextTraceFactory));

            // CORE TODO
            // Trace shutdown event
            // Cannot use shutdownDetector.Token.Register because of race condition
            // with NinjectServices.Stop via WebActivator.ApplicationShutdownMethodAttribute
            // Shutdown += () => TraceShutdown(environment, noContextDeploymentsSettingsManager);

            // LogStream service
            services.AddLogStreamService(_webAppRuntimeEnvironment,traceFactory);

            // Deployment Service
            services.AddWebJobsDependencies();

            services.AddScoped<ILogger>(KuduWebUtil.GetDeploymentLogger);

            services.AddScoped<IDeploymentManager, DeploymentManager>();
            
            services.AddScoped<IFetchDeploymentManager, FetchDeploymentManager>();

            services.AddScoped<IScanManager, ScanManager>();
            
            services.AddScoped<ISSHKeyManager, SSHKeyManager>();

            services.AddScoped<IRepositoryFactory>(
                sp => KuduWebUtil.GetDeploymentLock(traceFactory, environment).RepositoryFactory =
                    new RepositoryFactory(
                        sp.GetRequiredService<IEnvironment>(), sp.GetRequiredService<IDeploymentSettingsManager>(),
                        sp.GetRequiredService<ITraceFactory>()));

            services.AddScoped<IApplicationLogsReader, ApplicationLogsReader>();

            // Git server
            services.AddGitServer(KuduWebUtil.GetDeploymentLock(traceFactory, environment));

            // Git Servicehook Parsers
            services.AddGitServiceHookParsers();

            services.AddScoped<ICommandExecutor, CommandExecutor>();

            // Required for proxying requests to dotnet-monitor. Increase
            // the default time to a bigger number because memory dump requests
            // can take a few minutes to finish
            services.AddHttpClient("DotnetMonitorProxyClient", client =>
            {
                client.Timeout = TimeSpan.FromMinutes(10);
            });

            services.AddProxies();

            // KuduWebUtil.MigrateSite(environment, noContextDeploymentsSettingsManager);
            // RemoveOldTracePath(environment);
            // RemoveTempFileFromUserDrive(environment);

            // CORE TODO Windows Fix: Temporary fix for https://github.com/npm/npm/issues/5905
            //EnsureNpmGlobalDirectory();
            //EnsureUserProfileDirectory();

            //// Skip SSL Certificate Validate
            //if (Environment.SkipSslValidation)
            //{
            //    ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
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
            //String logfile = _webAppRuntimeEnvironment.LogFilesPath + "/.txt";
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
            Console.WriteLine(@"Configure : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));

            loggerFactory.AddEventSourceLogger();

            KuduWebUtil.MigrateToNetCorePatch(_webAppRuntimeEnvironment);

            if (_hostingEnvironment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            if (_webAppRuntimeEnvironment.IsOnLinuxConsumption)
            {
                app.UseLinuxConsumptionRouteMiddleware();
                app.UseMiddleware<EnvironmentReadyCheckMiddleware>();
            }

            var webSocketOptions = new WebSocketOptions()
            {
                KeepAliveInterval = TimeSpan.FromSeconds(15)
            };
            app.UseWebSockets(webSocketOptions);

            var containsRelativePath = new Func<HttpContext, bool>(i =>
                i.Request.Path.Value.StartsWith("/Default", StringComparison.OrdinalIgnoreCase));

            app.MapWhen(containsRelativePath, application => application.Run(async context =>
            {
                await context.Response.WriteAsync("Kestrel Running");
            }));

            var containsRelativePath2 = new Func<HttpContext, bool>(i =>
                i.Request.Path.Value.StartsWith("/info", StringComparison.OrdinalIgnoreCase));

            app.MapWhen(containsRelativePath2,
                application => application.Run(async context =>
                {
                    await context.Response.WriteAsync("{\"Version\":\"" + Constants.KuduBuild + "\"}");
                }));

            app.UseResponseCompression();

            var containsRelativePath3 = new Func<HttpContext, bool>(i =>
                i.Request.Path.Value.StartsWith("/AppServiceTunnel/Tunnel.ashx", StringComparison.OrdinalIgnoreCase));

            app.MapWhen(containsRelativePath3, builder => builder.UseMiddleware<DebugExtensionMiddleware>());

            app.UseTraceMiddleware();

            applicationLifetime.ApplicationStopping.Register(OnShutdown);

            app.UseStaticFiles();

            ProxyRequestIfRelativeUrlMatches(@"/webssh", "http", "127.0.0.1", KuduWebUtil.GetWebSSHProxyPort() , app);

            var configuration = app.ApplicationServices.GetRequiredService<IServerConfiguration>();

            // CORE TODO any equivalent for this? Needed?
            //var configuration = kernel.Get<IServerConfiguration>();
            //GlobalConfiguration.Configuration.Formatters.Clear();
            //GlobalConfiguration.Configuration.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;

            var jsonFormatter = new JsonMediaTypeFormatter();


            // CORE TODO concept of "deprecation" in routes for traces, Do we need this for linux ?

            // Push url
            foreach (var url in new[] {"/git-receive-pack", $"/{configuration.GitServerRoot}/git-receive-pack"})
            {
                app.Map(url, appBranch => appBranch.RunReceivePackHandler());
            }

            // Fetch hook
            app.Map("/deploy", appBranch => appBranch.RunFetchHandler());

            // Log streaming
            app.Map("/api/logstream", appBranch => appBranch.RunLogStreamHandler());


            // Clone url
            foreach (var url in new[] {"/git-upload-pack", $"/{configuration.GitServerRoot}/git-upload-pack"})
            {
                app.Map(url, appBranch => appBranch.RunUploadPackHandler());
            }

            // Custom GIT repositories, which can be served from any directory that has a git repo
            foreach (var url in new[] {"/git-custom-repository", "/git/{*path}"})
            {
                app.Map(url, appBranch => appBranch.RunCustomGitRepositoryHandler());
            }

            // Sets up the file server to web app's wwwroot
            KuduWebUtil.SetupFileServer(app, _webAppRuntimeEnvironment.WebRootPath, "/wwwroot");
            
            // Sets up the file server to LogFiles
            KuduWebUtil.SetupFileServer(app, Path.Combine(_webAppRuntimeEnvironment.LogFilesPath,"kudu","deployment"), "/deploymentlogs");

            app.UseSwagger();

            app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "Kudu API Docs"); });

            app.UseMvc(routes =>
            {
                Console.WriteLine(@"Setting Up Routes : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));
                routes.MapRouteAnalyzer("/routes"); // Add

                routes.MapRoute(
                    name: "default",
                    template: "{controller}/{action=Index}/{id?}");

                // Git Service
                routes.MapRoute("git-info-refs-root", "info/refs", new {controller = "InfoRefs", action = "Execute"});
                routes.MapRoute("git-info-refs", configuration.GitServerRoot + "/info/refs",
                    new {controller = "InfoRefs", action = "Execute"});

                // Scm (deployment repository)
                routes.MapHttpRouteDual("scm-info", "scm/info",
                    new {controller = "LiveScm", action = "GetRepositoryInfo"});
                routes.MapHttpRouteDual("scm-clean", "scm/clean", new {controller = "LiveScm", action = "Clean"});
                routes.MapHttpRouteDual("scm-delete", "scm", new {controller = "LiveScm", action = "Delete"},
                    new {verb = new HttpMethodRouteConstraint("DELETE")});


                // Scm files editor
                routes.MapHttpRouteDual("scm-get-files", "scmvfs/{*path}",
                    new {controller = "LiveScmEditor", action = "GetItem"},
                    new {verb = new HttpMethodRouteConstraint("GET", "HEAD")});
                routes.MapHttpRouteDual("scm-put-files", "scmvfs/{*path}",
                    new {controller = "LiveScmEditor", action = "PutItem"},
                    new {verb = new HttpMethodRouteConstraint("PUT")});
                routes.MapHttpRouteDual("scm-delete-files", "scmvfs/{*path}",
                    new {controller = "LiveScmEditor", action = "DeleteItem"},
                    new {verb = new HttpMethodRouteConstraint("DELETE")});

                // Live files editor
                routes.MapHttpRouteDual("vfs-get-files", "vfs/{*path}", new {controller = "Vfs", action = "GetItem"},
                    new {verb = new HttpMethodRouteConstraint("GET", "HEAD")});
                routes.MapHttpRouteDual("vfs-put-files", "vfs/{*path}", new {controller = "Vfs", action = "PutItem"},
                    new {verb = new HttpMethodRouteConstraint("PUT")});
                routes.MapHttpRouteDual("vfs-delete-files", "vfs/{*path}",
                    new {controller = "Vfs", action = "DeleteItem"},
                    new {verb = new HttpMethodRouteConstraint("DELETE")});

                // Zip file handler
                routes.MapHttpRouteDual("zip-get-files", "zip/{*path}", new {controller = "Zip", action = "GetItem"},
                    new {verb = new HttpMethodRouteConstraint("GET", "HEAD")});
                routes.MapHttpRouteDual("zip-put-files", "zip/{*path}", new {controller = "Zip", action = "PutItem"},
                    new {verb = new HttpMethodRouteConstraint("PUT")});

                // Zip push deployment
                routes.MapRoute("zip-push-deploy-arm", "zipdeploy",
                    new { controller = "PushDeployment", action = "ZipPushDeploy" },
                    new { verb = new HttpMethodRouteConstraint("POST") });
                routes.MapRoute("zip-push-deploy-url-arm", "zipdeploy",
                    new { controller = "PushDeployment", action = "ZipPushDeployViaUrl" },
                    new { verb = new HttpMethodRouteConstraint("PUT") });
                routes.MapRoute("zip-push-deploy", "api/zipdeploy",
                    new {controller = "PushDeployment", action = "ZipPushDeploy"},
                    new {verb = new HttpMethodRouteConstraint("POST")});
                routes.MapRoute("zip-push-deploy-url", "api/zipdeploy",
                    new {controller = "PushDeployment", action = "ZipPushDeployViaUrl"},
                    new {verb = new HttpMethodRouteConstraint("PUT")});
                routes.MapRoute("zip-war-deploy", "api/wardeploy",
                    new {controller = "PushDeployment", action = "WarPushDeploy"},
                    new {verb = new HttpMethodRouteConstraint("POST")});

                routes.MapHttpRouteDual("onedeploy", "publish",
                    new { controller = "PushDeployment", action = "OneDeploy" });

                // Support Linux Consumption Function app on Service Fabric Mesh
                routes.MapRoute("admin-instance-info", "admin/instance/info",
                    new {controller = "LinuxConsumptionInstanceAdmin", action = "Info"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapRoute("admin-instance-assign", "admin/instance/assign",
                    new {controller = "LinuxConsumptionInstanceAdmin", action = "AssignAsync" },
                    new {verb = new HttpMethodRouteConstraint("POST")});
                routes.MapRoute("admin-proxy-health-check", "admin/proxy/health-check",
                    new { controller = "LinuxConsumptionInstanceAdmin", action = "HttpHealthCheck" },
                    new { verb = new HttpMethodRouteConstraint("GET") });
                routes.MapRoute("admin-proxy-eviction-status", "admin/proxy/eviction-status",
                   new { controller = "LinuxConsumptionInstanceAdmin", action = "EvictionStatus" },
                   new { verb = new HttpMethodRouteConstraint("GET") });

                // Live Command Line
                routes.MapHttpRouteDual("execute-command", "command",
                    new {controller = "Command", action = "ExecuteCommand"},
                    new {verb = new HttpMethodRouteConstraint("POST")});

                // Deployments
                routes.MapHttpRouteDual("all-deployments", "deployments",
                    new {controller = "Deployment", action = "GetDeployResults"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpRouteDual("one-deployment-get", "deployments/{id}",
                    new {controller = "Deployment", action = "GetResult"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpRouteDual("one-deployment-put", "deployments/{id?}",
                    new {controller = "Deployment", action = "Deploy"},
                    new {verb = new HttpMethodRouteConstraint("PUT")});
                routes.MapHttpRouteDual("one-deployment-delete", "deployments/{id}",
                    new {controller = "Deployment", action = "Delete"},
                    new {verb = new HttpMethodRouteConstraint("DELETE")});
                routes.MapHttpRouteDual("one-deployment-log", "deployments/{id}/log",
                    new {controller = "Deployment", action = "GetLogEntry"});
                routes.MapHttpRouteDual("one-deployment-log-details", "deployments/{id}/log/{logId}",
                    new {controller = "Deployment", action = "GetLogEntryDetails"});

                // Deployment script
                routes.MapRoute("get-deployment-script", "api/deploymentscript",
                    new {controller = "Deployment", action = "GetDeploymentScript"},
                    new {verb = new HttpMethodRouteConstraint("GET")});

                // IsDeploying status 
                routes.MapRoute("is-deployment-underway", "api/isdeploying",
                    new {controller = "Deployment", action = "IsDeploying"},
                    new {verb = new HttpMethodRouteConstraint("GET")});

                // Initiate Scan 
                routes.MapRoute("start-clamscan", "api/scan/start/",
                    new { controller = "Scan", action = "ExecuteScan" },
                    new { verb = new HttpMethodRouteConstraint("GET") });

                //Get scan status
                routes.MapRoute("get-scan-status", "/api/scan/{scanId}/track",
                    new { controller = "Scan", action = "GetScanStatus" },
                    new { verb = new HttpMethodRouteConstraint("GET") });

                //Get unique scan result
                routes.MapRoute("get-scan-result", "/api/scan/{scanId}/result",
                    new { controller = "Scan", action = "GetScanLog" },
                    new { verb = new HttpMethodRouteConstraint("GET") });

                //Get all scan result
                routes.MapRoute("get-all-scan-result", "/api/scan/results",
                    new { controller = "Scan", action = "GetScanResults" },
                    new { verb = new HttpMethodRouteConstraint("GET") });

                //Stop scan
                routes.MapRoute("stop-scan", "/api/scan/stop",
                    new { controller = "Scan", action = "StopScan" },
                    new { verb = new HttpMethodRouteConstraint("DELETE") });

                // SSHKey
                routes.MapHttpRouteDual("get-sshkey", "api/sshkey",
                    new {controller = "SSHKey", action = "GetPublicKey"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpRouteDual("put-sshkey", "api/sshkey",
                    new {controller = "SSHKey", action = "SetPrivateKey"},
                    new {verb = new HttpMethodRouteConstraint("PUT")});
                routes.MapHttpRouteDual("delete-sshkey", "api/sshkey",
                    new {controller = "SSHKey", action = "DeleteKeyPair"},
                    new {verb = new HttpMethodRouteConstraint("DELETE")});

                // Environment
                routes.MapHttpRouteDual("get-env", "environment", new {controller = "Environment", action = "Get"},
                    new {verb = new HttpMethodRouteConstraint("GET")});

                // Settings
                routes.MapHttpRouteDual("set-setting", "settings", new {controller = "Settings", action = "Set"},
                    new {verb = new HttpMethodRouteConstraint("POST")});
                routes.MapHttpRouteDual("get-all-settings", "settings",
                    new {controller = "Settings", action = "GetAll"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpRouteDual("get-setting", "settings/{key}", new {controller = "Settings", action = "Get"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpRouteDual("delete-setting", "settings/{key}",
                    new {controller = "Settings", action = "Delete"},
                    new {verb = new HttpMethodRouteConstraint("DELETE")});

                // Diagnostics
                routes.MapHttpRouteDual("diagnostics", "dump", new {controller = "Diagnostics", action = "GetLog"});
                routes.MapHttpRouteDual("diagnostics-set-setting", "diagnostics/settings",
                    new {controller = "Diagnostics", action = "Set"},
                    new {verb = new HttpMethodRouteConstraint("POST")});
                routes.MapHttpRouteDual("diagnostics-get-all-settings", "diagnostics/settings",
                    new {controller = "Diagnostics", action = "GetAll"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpRouteDual("diagnostics-get-setting", "diagnostics/settings/{key}",
                    new {controller = "Diagnostics", action = "Get"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpRouteDual("diagnostics-delete-setting", "diagnostics/settings/{key}",
                    new {controller = "Diagnostics", action = "Delete"},
                    new {verb = new HttpMethodRouteConstraint("DELETE")});

                // Logs
                foreach (var url in new[] {"/logstream", "/logstream/{*path}"})
                {
                    app.Map(url, appBranch => appBranch.RunLogStreamHandler());
                }

                routes.MapHttpRouteDual("recent-logs", "api/logs/recent",
                    new {controller = "Diagnostics", action = "GetRecentLogs"},
                    new {verb = new HttpMethodRouteConstraint("GET")});

                // Enable these for Linux and Windows Containers.
                if (!OSDetector.IsOnWindows() || (OSDetector.IsOnWindows() && EnvironmentHelper.IsWindowsContainers()))
                {
                    routes.MapRoute("current-docker-logs-zip", "api/logs/docker/zip",
                        new {controller = "Diagnostics", action = "GetDockerLogsZip"},
                        new {verb = new HttpMethodRouteConstraint("GET")});
                    routes.MapRoute("current-docker-logs", "api/logs/docker",
                        new {controller = "Diagnostics", action = "GetDockerLogs"},
                        new {verb = new HttpMethodRouteConstraint("GET")});
                }

                var processControllerName = OSDetector.IsOnWindows() ? "Process" : "LinuxProcess";

                // Processes
                routes.MapHttpProcessesRoute("all-processes", "",
                    new {controller = processControllerName, action = "GetAllProcesses"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpProcessesRoute("one-process-get", "/{id}",
                    new {controller = processControllerName, action = "GetProcess"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpProcessesRoute("one-process-delete", "/{id}",
                    new {controller = processControllerName, action = "KillProcess"},
                    new {verb = new HttpMethodRouteConstraint("DELETE")});
                routes.MapHttpProcessesRoute("one-process-dump", "/{id}/dump",
                    new {controller = processControllerName, action = "MiniDump"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpProcessesRoute("start-process-profile-get", "/{id}/profile/start",
                    new {controller = processControllerName, action = "StartProfileAsync"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpProcessesRoute("start-process-profile", "/{id}/profile/start",
                    new { controller = processControllerName, action = "StartProfileAsync" },
                    new { verb = new HttpMethodRouteConstraint("POST") });
                routes.MapHttpProcessesRoute("stop-process-profile", "/{id}/profile/stop",
                    new {controller = processControllerName, action = "StopProfileAsync"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpProcessesRoute("all-threads", "/{id}/threads",
                    new {controller = processControllerName, action = "GetAllThreads"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpProcessesRoute("one-process-thread", "/{processId}/threads/{threadId}",
                    new {controller = processControllerName, action = "GetThread"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpProcessesRoute("all-modules", "/{id}/modules",
                    new {controller = processControllerName, action = "GetAllModules"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpProcessesRoute("one-process-module", "/{id}/modules/{baseAddress}",
                    new {controller = processControllerName, action = "GetModule"},
                    new {verb = new HttpMethodRouteConstraint("GET")});
                routes.MapHttpProcessesRoute("all-envs", "/{id}/environments/{filter}",
                    new { controller = processControllerName, action = "GetEnvironments" },
                    new { verb = new HttpMethodRouteConstraint("GET") });

                // Runtime
                routes.MapHttpRouteDual("runtime", "diagnostics/runtime",
                    new {controller = "Runtime", action = "GetRuntimeVersions"},
                    new {verb = new HttpMethodRouteConstraint("GET")});

                // Docker Hook Endpoint
                if (!OSDetector.IsOnWindows() || (OSDetector.IsOnWindows() && EnvironmentHelper.IsWindowsContainers()))
                {
                    routes.MapHttpRouteDual("docker", "docker/hook",
                        new {controller = "Docker", action = "ReceiveHook"},
                        new {verb = new HttpMethodRouteConstraint("POST")});
                }

                // catch all unregistered url to properly handle not found
                routes.MapRoute("error-404", "{*path}", new {controller = "Error404", action = "Handle"});
            });

            Console.WriteLine(@"Exiting Configure : " + DateTime.Now.ToString("hh.mm.ss.ffffff"));
        }

        // <summary>
        // Used for Reverse Proxying. Forwards a request to another path based on a
        // relative path to Kestrel
        // </summary>
        // <param name="app">
        // Object to configure an application's request pipeline.
        // </param>
        // <param name="relativeUrl">
        // String is matched if the request URI path is prepended by it
        // </param>
        // <param name="scheme">
        // A String that contains the scheme for the proxy URI
        // </param>
        // <param name="host">
        //  A String that contains the host for the proxy URI
        // </param>
        // <param name="port">
        //  A String that contains the host for the proxy URI. Cannot be null
        // </param>
        private static void ProxyRequestIfRelativeUrlMatches(
            string relativeUrl,
            string scheme,
            string host,
            string port,
            IApplicationBuilder app)
        {
            var containsRelativePath = new Func<HttpContext, bool>(i =>
                i.Request.Path.Value.StartsWith(relativeUrl, StringComparison.OrdinalIgnoreCase));
            app.MapWhen(containsRelativePath, builder => builder.RunProxy(new ProxyOptions
            {
                Scheme = scheme,
                Host = host,
                Port = port
            }));
        }

        // <summary>
        // Returns a lambda function that checks if an incoming request object's url path
        // is prepended by a string
        // </summary>
        // <param name="relativeUrl">
        // String keyword with which incoming request's path is matched
        // </param>
        // <param name="httpContext">
        // The HttpContext object of an incoming request
        // </param>
        private static Func<HttpContext, bool> ContainsRelativeUrl(string relativeUrl, HttpContext httpContext)
        {
            return x => httpContext.Request.Path.Value.StartsWith(relativeUrl, StringComparison.OrdinalIgnoreCase);
        }

        // <summary>
        // Returns a lambda function that checks if an incoming request object's url path
        // is prepended by a string
        // </summary>
        // <param name="relativeUrl">
        // String keyword with which incoming request's path is matched
        // </param>
        // <param name="httpContext">
        // The HttpContext object of an incoming request
        // </param>
        private void OnShutdown()
        {
            KuduWebUtil.TraceShutdown(_webAppRuntimeEnvironment, _noContextDeploymentsSettingsManager);
            // Cleaning up deployment locks
            Console.WriteLine(@"Removing Deployment Locks");
            FileSystemHelpers.DeleteDirectorySafe("/home/site/locks/deployment");
            Console.WriteLine(@"Shutting Down!");
        }
    }
}
