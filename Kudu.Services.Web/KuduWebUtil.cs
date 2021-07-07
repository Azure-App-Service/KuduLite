using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Deployment;
using Kudu.Core.Helpers;
using Kudu.Core.Infrastructure;
using Kudu.Core.Settings;
using Kudu.Core.Tracing;
using Kudu.Services.Infrastructure;
using Kudu.Services.Infrastructure.Authorization;
using Kudu.Services.Infrastructure.Authentication;
using Kudu.Services.Web.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Authorization;
using Environment = Kudu.Core.Environment;
using Org.BouncyCastle.Asn1.Ocsp;

namespace Kudu.Services.Web
{
    internal static class KuduWebUtil
    {
        private const string KuduConsoleFilename = "kudu.dll";
        private const string KuduConsoleRelativePath = "KuduConsole";

        private static Dictionary<string, IOperationLock> _namedLocks;

        private static IOperationLock _deploymentLock;

        // <summary>
        // This method initializes status,ssh,hooks & deployment locks used by Kudu to ensure
        // synchronized operations. This method creates a dictionary of locks which is injected
        // into various controllers to resolve the locks they need.
        // <list type="bullet">  
        //     <listheader>  
        //         <term>Locks used by Kudu:</term>  
        //     </listheader>  
        //     <item>  
        //         <term>Status Lock</term>  
        //         <description>Used by DeploymentStatusManager</description>  
        //     </item> 
        //     <item>  
        //         <term>SSH Lock</term>  
        //         <description>Used by SSHKeyController</description>  
        //     </item> 
        //     <item>  
        //         <term>Hooks Lock</term>  
        //         <description>Used by WebHooksManager</description>  
        //     </item> 
        //     <item>  
        //         <term>Deployment Lock</term>  
        //         <description>
        //             Used by DeploymentController, DeploymentManager, SettingsController, 
        //             FetchDeploymentManager, LiveScmController, ReceivePackHandlerMiddleware
        //         </description>  
        //     </item> 
        // </list>  
        // </summary>
        // <remarks>
        //     Uses File watcher.
        //     This originally used Ninject's "WhenInjectedInto" in .Net project for specific instances. IServiceCollection
        //     doesn't support this concept, or anything similar like named instances. There are a few possibilities, 
        //     but the hack solution for now is just injecting a dictionary of locks and letting each dependent resolve 
        //     the one it needs.
        // </remarks>
        private static void SetupLocks(ITraceFactory traceFactory, IEnvironment environment)
        {
            var lockPath = Path.Combine(environment.SiteRootPath, Constants.LockPath);
            var deploymentLockPath = Path.Combine(lockPath, Constants.DeploymentLockFile);
            var statusLockPath = Path.Combine(lockPath, Constants.StatusLockFile);
            var sshKeyLockPath = Path.Combine(lockPath, Constants.SSHKeyLockFile);
            var hooksLockPath = Path.Combine(lockPath, Constants.HooksLockFile);
            _deploymentLock = new NoOpLock();
            var statusLock = new LockFile(statusLockPath, traceFactory);
            statusLock.InitializeAsyncLocks();
            var sshKeyLock = new LockFile(sshKeyLockPath, traceFactory);
            sshKeyLock.InitializeAsyncLocks();
            var hooksLock = new LockFile(hooksLockPath, traceFactory);
            hooksLock.InitializeAsyncLocks();

            _namedLocks = new Dictionary<string, IOperationLock>
            {
                {"status", statusLock},
                {"ssh", sshKeyLock},
                {"hooks", hooksLock},
                {"deployment", _deploymentLock}
            };
        }


        internal static void SetupFileServer(IApplicationBuilder app, string fileDirectoryPath, string requestPath)
        {

            // Create deployment logs directory if it doesn't exist
            FileSystemHelpers.CreateDirectory(fileDirectoryPath);
            
            // Set up custom content types - associating file extension to MIME type
            var provider = new FileExtensionContentTypeProvider
            {
                Mappings =
                {
                    [".py"] = "text/html",
                    [".env"] = "text/html",
                    [".cshtml"] = "text/html",
                    [".log"] = "text/html",
                    [".image"] = "image/png"
                }
            };

            app.UseFileServer(new FileServerOptions
            {
                FileProvider = new PhysicalFileProvider(
                    fileDirectoryPath),
                RequestPath = requestPath,
                EnableDirectoryBrowsing = true,
                StaticFileOptions =
                {
                    ServeUnknownFileTypes = true,
                    DefaultContentType = "text/plain",
                    ContentTypeProvider = provider
                }
            });
        }

        /// <summary>
        /// Returns absolute URL for a request including host, path and the query string
        /// </summary>
        /// <param name="httpContext">
        /// Object encapsulating all HTTP-specific information about an individual HTTP request.
        /// </param>
        /// <returns></returns>
        internal static Uri GetAbsoluteUri(HttpContext httpContext)
        {
            var request = httpContext.Request;
            var uriBuilder = new UriBuilder
            {
                Scheme = request.Scheme,
                Host = request.Host.Host,
                Path = request.Path.ToString(),
                Query = request.QueryString.ToString()
            };
            return uriBuilder.Uri;
        }

        /// <summary>
        /// Returns the tracer objects for request tracing
        /// </summary>
        internal static ITracer GetTracer(IServiceProvider serviceProvider)
        {
            var environment = serviceProvider.GetRequiredService<IEnvironment>();
            var level = serviceProvider.GetRequiredService<IDeploymentSettingsManager>().GetTraceLevel();
            var contextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
            var httpContext = contextAccessor.HttpContext;
            var requestTraceFile = TraceServices.GetRequestTraceFile(httpContext);
            if (level <= TraceLevel.Off || requestTraceFile == null) return NullTracer.Instance;
            var textPath = Path.Combine(environment.TracePath, requestTraceFile);
            return new CascadeTracer(new XmlTracer(environment.TracePath, level),
                new TextTracer(textPath, level));
        }

        /// <summary>
        /// Returns the name of the trace file. Each request is associated with a Tracing GUID, returns this guid.
        /// </summary>
        internal static string GetRequestTraceFile(IServiceProvider serviceProvider)
        {
            var traceLevel = serviceProvider.GetRequiredService<IDeploymentSettingsManager>().GetTraceLevel();
            if (traceLevel <= TraceLevel.Off) return null;
            var contextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
            var httpContext = contextAccessor.HttpContext;
            return TraceServices.GetRequestTraceFile(httpContext);
        }


        /// <summary>
        /// Ensures smooth transition between mono based Kudu and KuduLite.
        /// <remarks>
        /// <list type="bullet">
        ///     <item>
        ///         POST Receive GitHook File:This file was previously hard coded with mono path to launch kudu console.
        ///     </item>
        ///     <item>
        ///         We will would use the OryxBuild in future for deployments, as a safety measure we clear
        ///         the deployment script.
        ///     </item>
        /// </list>
        /// </remarks>
        /// </summary>
        /// <param name="environment"></param>
        internal static void MigrateToNetCorePatch(IEnvironment environment)
        {
	    // Get the repository path:
            // Use value in the settings.xml file if it is present.
            string repositoryPath = environment.RepositoryPath;
            IDeploymentSettingsManager settings = GetDeploymentSettingsManager(environment);
            if (settings != null)
            {
                var settingsRepoPath = DeploymentSettingsExtension.GetRepositoryPath(settings);
                repositoryPath = Path.Combine(environment.SiteRootPath, settingsRepoPath);
            }

            var gitPostReceiveHookFile = Path.Combine(repositoryPath, ".git", "hooks", "post-receive");
            if (FileSystemHelpers.FileExists(gitPostReceiveHookFile))
            {
                var fileText = FileSystemHelpers.ReadAllText(gitPostReceiveHookFile);
                var isRunningOnAzure = System.Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") != null;
                if (fileText.Contains("/usr/bin/mono"))
                {
                    if(isRunningOnAzure)
                    {
                        FileSystemHelpers.WriteAllText(gitPostReceiveHookFile, fileText.Replace("/usr/bin/mono", "benv dotnet=2.2 dotnet"));
                    }
                }
                else if(!fileText.Contains("benv") && fileText.Contains("dotnet") && isRunningOnAzure)
                {
                    FileSystemHelpers.WriteAllText(gitPostReceiveHookFile, fileText.Replace("dotnet", "benv dotnet=2.2 dotnet"));
                }
                
            }

            if (FileSystemHelpers.DirectoryExists(Path.Combine(environment.RootPath, ".mono"))
                && FileSystemHelpers.FileExists(Path.Combine(environment.DeploymentToolsPath, "deploy.sh")))
            {
                FileSystemHelpers.DeleteFileSafe(Path.Combine(environment.DeploymentToolsPath, "deploy.sh"));
            }
        }

        internal static void TraceShutdown(IEnvironment environment, IDeploymentSettingsManager settings)
        {
            ITracer tracer = GetTracerWithoutContext(environment, settings);
            var attribs = new Dictionary<string, string>();

            // Add an attribute containing the process, AppDomain and Thread ids to help debugging
            attribs.Add("pid", String.Format("{0},{1},{2}",
                Process.GetCurrentProcess().Id,
                AppDomain.CurrentDomain.Id.ToString(),
                Thread.CurrentThread.ManagedThreadId));

            attribs.Add("uptime", TraceMiddleware.UpTime.ToString());

            attribs.Add("lastrequesttime", TraceMiddleware.LastRequestTime.ToString());

            tracer.Trace(XmlTracer.ProcessShutdownTrace, attribs);

            OperationManager.SafeExecute(() =>
            {
                KuduEventSource.Log.GenericEvent(
                    ServerConfiguration.GetApplicationName(),
                    string.Format("Shutdown pid:{0}, domain:{1}", Process.GetCurrentProcess().Id,
                        AppDomain.CurrentDomain.Id),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty);
            });
        }

        internal static ITracer GetTracerWithoutContext(IEnvironment environment, IDeploymentSettingsManager settings)
        {
            // when file system has issue, this can throw (environment.TracePath calls EnsureDirectory).
            // prefer no-op tracer over outage.  
            return OperationManager.SafeExecute(() =>
            {
                var traceLevel = settings.GetTraceLevel();
                return traceLevel > TraceLevel.Off
                    ? new XmlTracer(environment.TracePath, traceLevel)
                    : NullTracer.Instance;
            }) ?? NullTracer.Instance;
        }

        /// <summary>
        /// Returns the ILogger object to log deployments
        /// </summary>
        internal static ILogger GetDeploymentLogger(IServiceProvider serviceProvider)
        {
            var environment = serviceProvider.GetRequiredService<IEnvironment>();
            var level = serviceProvider.GetRequiredService<IDeploymentSettingsManager>().GetTraceLevel();
            var contextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
            var httpContext = contextAccessor.HttpContext;
            var requestTraceFile = TraceServices.GetRequestTraceFile(httpContext);
            if (level <= TraceLevel.Off || requestTraceFile == null) return NullLogger.Instance;
            var textPath = Path.Combine(environment.DeploymentTracePath, requestTraceFile);
            return new TextLogger(textPath);
        }

        /// <summary>
        /// Returns a specified environment configuration as the current webapp's
        /// default configuration during the runtime.
        /// </summary>
        internal static IEnvironment GetEnvironment(IHostingEnvironment hostingEnvironment,
            IDeploymentSettingsManager settings = null,
            HttpContext httpContext = null)
        {
            var root = PathResolver.ResolveRootPath();
            var siteRoot = Path.Combine(root, Constants.SiteFolder);
            var repositoryPath = Path.Combine(siteRoot,
                settings == null ? Constants.RepositoryPath : settings.GetRepositoryPath());
            var binPath = AppContext.BaseDirectory;
            var requestId = httpContext != null ? httpContext.Request.GetRequestId() : null;
            var kuduConsoleFullPath =
                Path.Combine(AppContext.BaseDirectory, KuduConsoleRelativePath, KuduConsoleFilename);
            return new Environment(root, EnvironmentHelper.NormalizeBinPath(binPath), repositoryPath, requestId,
                kuduConsoleFullPath, null);
        }

        /// <summary>
        /// Ensures %HOME% is correctly set
        /// </summary>
        internal static void EnsureHomeEnvironmentVariable()
        {
            // PlatformServices.Default and the injected IHostingEnvironment have at runtime.
            if (Directory.Exists(System.Environment.ExpandEnvironmentVariables(@"%HOME%"))) return;

            //For Debug
            System.Environment.SetEnvironmentVariable("HOME", OSDetector.IsOnWindows() ? @"F:\kudu-debug" : "/home");

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

        /// <summary>
        /// Ensures valid /home/site/deployments/settings.xml is loaded, deletes
        /// corrupt settings.xml file
        /// </summary>
        /// <param name="environment">
        /// IEnvironment object that maintains paths used by kudu 
        /// </param>
        internal static void EnsureValidDeploymentXmlSettings(IEnvironment environment)
        {
            var path = GetSettingsPath(environment);
            if (!FileSystemHelpers.FileExists(path)) return;
            try
            {
                var settings = new DeploymentSettingsManager(new XmlSettings.Settings(path));
                settings.GetValue(SettingsKeys.TraceLevel);
            }
            catch (Exception ex)
            {
                DateTime lastWriteTimeUtc = DateTime.MinValue;
                OperationManager.SafeExecute(() => lastWriteTimeUtc = File.GetLastWriteTimeUtc(path));
                // trace initialization error
                KuduEventSource.Log.KuduException(
                    ServerConfiguration.GetApplicationName(),
                    "Startup.cs",
                    string.Empty,
                    string.Empty,
                    string.Format("Invalid '{0}' is detected and deleted.  Last updated time was {1}.", path,
                        lastWriteTimeUtc),
                    ex.ToString());
                File.Delete(path);
            }
        }

	/// <summary>
        /// Get Deploployment settings
        /// </summary>
        /// <param name="environment"></param>
        private static IDeploymentSettingsManager GetDeploymentSettingsManager(IEnvironment environment)
        {
            var path = GetSettingsPath(environment);
            if (!FileSystemHelpers.FileExists(path))
            {
                return null;
            }

            IDeploymentSettingsManager result = null;

            try
            {
                var settings = new DeploymentSettingsManager(new XmlSettings.Settings(path));
                settings.GetValue(SettingsKeys.TraceLevel);

                result = settings;
            }
            catch (Exception ex)
            {
                DateTime lastWriteTimeUtc = DateTime.MinValue;
                OperationManager.SafeExecute(() => lastWriteTimeUtc = File.GetLastWriteTimeUtc(path));
                // trace initialization error
                KuduEventSource.Log.KuduException(
                    ServerConfiguration.GetApplicationName(),
                    "Startup.cs",
                    string.Empty,
                    string.Empty,
                    string.Format("Invalid '{0}' is detected and deleted.  Last updated time was {1}.", path,
                        lastWriteTimeUtc),
                    ex.ToString());
                File.Delete(path);
            }

            return result;
        }

        internal static void PrependFoldersToPath(IEnvironment environment)
        {
            var folders = PathUtilityFactory.Instance.GetPathFolders(environment);

            var path = System.Environment.GetEnvironmentVariable("PATH");
            // Ignore any folder that doesn't actually exist
            var additionalPaths =
                string.Join(Path.PathSeparator.ToString(), folders.Where(Directory.Exists));

            // Make sure we haven't already added them. This can happen if the Kudu appdomain restart (since it's still same process)
            if (path != null && path.Contains(additionalPaths)) return;
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

        /// <summary>
        /// Sets the environment variables for Net Core CLI. 
        /// </summary>
        /// <remarks>
        /// This method previously included environment variables to optimize net core runtime to run in a container.
        /// But they have been moved to the Kudu Dockerfile
        /// </remarks>>
        /// <param name="environment">
        /// IEnvironment object that maintains references to al the paths used by Kudu during runtime.
        /// </param>
        internal static void EnsureDotNetCoreEnvironmentVariable(IEnvironment environment)
        {
            if (Environment.IsAzureEnvironment())
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

        /// <summary>
        /// Returns the singleton Deployment Lock Object
        /// </summary>
        /// <param name="traceFactory"></param>
        /// <param name="environment"></param>
        /// <returns></returns>
        internal static IOperationLock GetDeploymentLock(ITraceFactory traceFactory, IEnvironment environment)
        {
            if (_namedLocks == null || _deploymentLock == null)
            {
                GetNamedLocks(traceFactory, environment);
            }

            return _deploymentLock;
        }

        /// <summary>
        /// Returns a dictionary containing references to all the singleton lock objects. Initialises these locks
        /// if they have not been initialised 
        /// </summary>
        /// <param name="traceFactory"></param>
        /// <param name="environment"></param>
        /// <returns></returns>
        internal static Dictionary<string, IOperationLock> GetNamedLocks(ITraceFactory traceFactory,
            IEnvironment environment)
        {
            if (_namedLocks == null)
            {
                SetupLocks(traceFactory, environment);
            }

            return _namedLocks;
        }

        // <summary>
        // Adds an environment variable for site bitness. Can be "AMD64" or "x86";
        // </summary>
        internal static void EnsureSiteBitnessEnvironmentVariable()
        {
            SetEnvironmentVariableIfNotYetSet("SITE_BITNESS",
                System.Environment.Is64BitProcess ? Constants.X64Bit : Constants.X86Bit);
        }

        private static void SetEnvironmentVariableIfNotYetSet(string name, string value)
        {
            if (System.Environment.GetEnvironmentVariable(name) == null)
            {
                System.Environment.SetEnvironmentVariable(name, value);
            }
        }

        /// <summary>
        /// Returns the path to the settings.xml file in the deployments directory
        /// </summary>
        internal static string GetSettingsPath(IEnvironment environment)
        {
            return Path.Combine(environment.DeploymentsPath, Constants.DeploySettingsPath);
        }

        internal static IServiceCollection AddLinuxConsumptionAuthentication(this IServiceCollection services)
        {
            services.AddAuthentication()
                .AddArmToken();

            return services;
        }

        /// <summary>
        /// In Linux consumption, we are running the KuduLite instance in a Service Fabric Mesh container.
        /// We want to introduce AdminAuthLevel policy to restrict instance admin endpoint access.
        /// </summary>
        /// <param name="services">Dependency injection to application service</param>
        /// <returns>Service</returns>
        internal static IServiceCollection AddLinuxConsumptionAuthorization(this IServiceCollection services, IEnvironment environment)
        {
            services.AddAuthorization(o =>
            {
                o.AddInstanceAdminPolicies(environment);
            });

            services.AddSingleton<IAuthorizationHandler, AuthLevelAuthorizationHandler>();
            return services;
        }

        internal static string GetWebSSHProxyPort()
        {
            return System.Environment.GetEnvironmentVariable(Constants.WebSiteSwapSlotName) ?? Constants.WebSSHReverseProxyDefaultPort;
        }
    }
}
