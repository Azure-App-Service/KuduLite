using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Kudu.Console.Services;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Helpers;
using Kudu.Core.Hooks;
using Kudu.Core.Infrastructure;
using Kudu.Core.K8SE;
using Kudu.Core.Kube;
using Kudu.Core.Settings;
using Kudu.Core.SourceControl;
using Kudu.Core.SourceControl.Git;
using Kudu.Core.Tracing;
using log4net;
using log4net.Config;
using XmlSettings;
using IRepository = Kudu.Core.SourceControl.IRepository;

namespace Kudu.Console
{
    internal class Program
    {
        private static IEnvironment env;
        private static IDeploymentSettingsManager settingsManager;
        private static string appRoot;
        private static ITracer tracer;
        private static TraceLevel level;
        private static ITraceFactory traceFactory;
        private static ConsoleLogger logger = new ConsoleLogger();

        private static int Main(string[] args)
        {
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));
            // Turn flag on in app.config to wait for debugger on launch
            if (ConfigurationManager.AppSettings["WaitForDebuggerOnStart"] == "true")
            {
                while (!Debugger.IsAttached)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }

            if (System.Environment.GetEnvironmentVariable(SettingsKeys.DisableDeploymentOnPush) == "1")
            {
                return 0;
            }

            if (args.Length < 2)
            {
                //example: kudu.exe /home/apps/layliunode2/site /opt/Kudu/msbuild
                System.Console.WriteLine("Usage: kudu.exe appRoot wapTargets [deployer]");
                System.Console.WriteLine("Usage: kudu.exe appRoot buildType gitRepositoryUri [deployer]");
                return 1;
            }

            // The post receive hook launches the exe from sh and intereprets newline differently.
            // This fixes very wacky issues with how the output shows up in the console on push
            System.Console.Error.NewLine = "\n";
            System.Console.Out.NewLine = "\n";

            appRoot = args[0];
            string wapTargets = args[1];
            string deployer = args.Length == 2 ? null : args[2];

            env = GetEnvironment(appRoot);
            ISettings settings = new XmlSettings.Settings(GetSettingsPath());
            settingsManager = new DeploymentSettingsManager(settings);

            // Setup the trace
            level = settingsManager.GetTraceLevel();
            tracer = GetTracer();
            traceFactory = new TracerFactory(() => tracer);

            // Calculate the lock path
            string lockPath = Path.Combine(env.SiteRootPath, Constants.LockPath);
            string deploymentLockPath = Path.Combine(lockPath, Constants.DeploymentLockFile);

            IOperationLock deploymentLock = DeploymentLockFile.GetInstance(deploymentLockPath, traceFactory);

            if (K8SEDeploymentHelper.IsBuildJob())
            {
                string buildType = args[1];
                string repositoryUri = args[2];
                return RunBuildJob(buildType, repositoryUri, deployer, lockPath, deploymentLock);
            }

            if (K8SEDeploymentHelper.UseBuildJob())
            {
                if (deploymentLock.IsHeld)
                {
                    return BuildJobHelper.RunWithBuildJob(appRoot, env, "git", level, tracer).Result;
                }

                // Cross child process lock is not working on linux via mono.
                // When we reach here, deployment lock must be HELD! To solve above issue, we lock again before continue.
                try
                {
                    return deploymentLock.LockOperation(() =>
                    {
                        return BuildJobHelper.RunWithBuildJob(appRoot, env, "git", level, tracer).Result;
                    }, "DeployWithBuildJob", TimeSpan.Zero);
                }
                catch (LockOperationException)
                {
                    return -1;
                }
            }
            else
            {
                if (deploymentLock.IsHeld)
                {
                    return RunWithoutBuildJob(wapTargets, deployer, lockPath, deploymentLock);
                }

                // Cross child process lock is not working on linux via mono.
                // When we reach here, deployment lock must be HELD! To solve above issue, we lock again before continue.
                try
                {
                    return deploymentLock.LockOperation(() =>
                    {
                        return RunWithoutBuildJob(wapTargets, deployer, lockPath, deploymentLock);
                    }, "Performing deployment", TimeSpan.Zero);
                }
                catch (LockOperationException)
                {
                    return -1;
                }
            }
        }

        private static int RunBuildJob(string buildType, string repositoryUri, string deployer, string lockPath,
            IOperationLock deploymentLock)
        {
            var step = tracer.Step(XmlTracer.ExecutingExternalProcessTrace, new Dictionary<string, string>
            {
                { "type", "process" },
                { "path", "kudu.exe" },
                { "arguments", $"{appRoot} {buildType} {repositoryUri}" }
            });

            int result = 0;
            using (step)
            {
                result = PerformDeploy(deployer, lockPath, deploymentLock, () => PrepareRepositoryForBuildJob(buildType, repositoryUri));

                if (result == 0)
                {
                    BuildJobHelper.DeleteBuildJob(tracer);
                }
            }

            if (logger.HasErrors || result == 1)
            {
                return 1;
            }

            using (tracer.Step("Perform deploy exiting successfully")) { };
            return result;
        }

        private static int RunWithoutBuildJob(
            string wapTargets,
            string deployer,
            string lockPath,
            IOperationLock deploymentLock)
        {
            var step = tracer.Step(XmlTracer.ExecutingExternalProcessTrace, new Dictionary<string, string>
            {
                { "type", "process" },
                { "path", "kudu.exe" },
                { "arguments", $"{appRoot} {wapTargets}" }
            });

            int result = 0;

            using (step)
            {
                Func<IRepository> getRepository = () =>
                    {
                        if (settingsManager.UseLibGit2SharpRepository())
                        {
                            return new LibGit2SharpRepository(env, settingsManager, traceFactory);
                        }
                        else
                        {
                            return new GitExeRepository(env, settingsManager, traceFactory);
                        }
                    };

                result = PerformDeploy(deployer, lockPath, deploymentLock, getRepository);
            }

            if (logger.HasErrors || result == 1)
            {
                return 1;
            }

            using (tracer.Step("Perform deploy exiting successfully")) { };
            return result;
        }

        private static int PerformDeploy(
            string deployer,
            string lockPath,
            IOperationLock deploymentLock,
            Func<IRepository> getRepository)
        {

            System.Environment.SetEnvironmentVariable("GIT_DIR", null, System.EnvironmentVariableTarget.Process);

            // Skip SSL Certificate Validate
            if (System.Environment.GetEnvironmentVariable(SettingsKeys.SkipSslValidation) == "1")
            {
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            }

            // Adjust repo path
            env.RepositoryPath = Path.Combine(env.SiteRootPath, settingsManager.GetRepositoryPath());
            string statusLockPath = Path.Combine(lockPath, Constants.StatusLockFile);
            string hooksLockPath = Path.Combine(lockPath, Constants.HooksLockFile);

            IOperationLock statusLock = new LockFile(statusLockPath, traceFactory);
            IOperationLock hooksLock = new LockFile(hooksLockPath, traceFactory);

            IBuildPropertyProvider buildPropertyProvider = new BuildPropertyProvider();
            ISiteBuilderFactory builderFactory = new SiteBuilderFactory(buildPropertyProvider, env, null);

            var repository = getRepository();

            env.CurrId = repository.GetChangeSet(settingsManager.GetBranch()).Id;

            IServerConfiguration serverConfiguration = new ServerConfiguration();

            IAnalytics analytics = new Analytics(settingsManager, serverConfiguration, traceFactory);

            IWebHooksManager hooksManager = new WebHooksManager(tracer, env, hooksLock);

            IDeploymentStatusManager deploymentStatusManager = new DeploymentStatusManager(env, analytics, statusLock);

            IDeploymentManager deploymentManager = new DeploymentManager(builderFactory,
                                                          env,
                                                          traceFactory,
                                                          analytics,
                                                          settingsManager,
                                                          deploymentStatusManager,
                                                          deploymentLock,
                                                          GetLogger(),
                                                          hooksManager,
                                                          null); // K8 todo

            try
            {
                // although the api is called DeployAsync, most expensive works are done synchronously.
                // need to launch separate task to go async explicitly (consistent with FetchDeploymentManager)
                var deploymentTask = Task.Run(async () => await deploymentManager.DeployAsync(repository, changeSet: null, deployer: deployer, clean: false));

#pragma warning disable 4014
                // Track pending task
                PostDeploymentHelper.TrackPendingOperation(deploymentTask, TimeSpan.Zero);
#pragma warning restore 4014

                deploymentTask.Wait();

                if (PostDeploymentHelper.IsAutoSwapEnabled())
                {
                    string branch = settingsManager.GetBranch();
                    ChangeSet changeSet = repository.GetChangeSet(branch);
                    IDeploymentStatusFile statusFile = deploymentStatusManager.Open(changeSet.Id, env);
                    if (statusFile != null && statusFile.Status == DeployStatus.Success)
                    {
                        PostDeploymentHelper.PerformAutoSwap(env.RequestId,
                                new PostDeploymentTraceListener(tracer, deploymentManager.GetLogger(changeSet.Id)))
                            .Wait();
                    }
                }
            }
            catch (Exception e)
            {
                System.Console.WriteLine(e.InnerException);
                tracer.TraceError(e);
                System.Console.Error.WriteLine(e.GetBaseException().Message);
                System.Console.Error.WriteLine(Resources.Log_DeploymentError);
                return 1;
            }
            finally
            {
                System.Console.WriteLine("Deployment Logs : '" +
                env.AppBaseUrlPrefix + "/newui/jsonviewer?view_url=/api/deployments/" +
                repository.GetChangeSet(settingsManager.GetBranch()).Id + "/log'");
            }
            return 0;
        }

        private static IRepository PrepareRepositoryForBuildJob(string buildType, string repositoryUri)
        {
            try
            {
                var authKey = System.Environment.GetEnvironmentVariable(Constants.WebSiteAuthEncryptionKey);
                var token = AuthHelper.CreateToken(authKey);
                if (buildType == "zip")
                {
                    DeploymentFileHelper helper = new DeploymentFileHelper(null, null, token, tracer);
                    Uri uri = new Uri(repositoryUri);
                    var localFilePath = Path.Combine(env.ZipTempPath, uri.Segments.Last());
                    helper.Download(localFilePath, repositoryUri).Wait();

                    ArtifactDeploymentInfo zipDeploymentInfo = new ArtifactDeploymentInfo(env, traceFactory)
                    {
                        RepositoryUrl = localFilePath,
                    };

                    IRepository repository = zipDeploymentInfo.GetRepository();
                    DeploymentHelper.LocalZipFetch(env.ZipTempPath, zipDeploymentInfo.GetRepository(), zipDeploymentInfo, logger, tracer).Wait();

                    return repository;
                }
                else if (buildType == "git")
                {
                    var repository = new GitExeRepository(env, settingsManager, traceFactory)
                    {
                        SkipPostReceiveHookCheck = true
                    };

                    repository.Initialize();
                    repository.ConfigExtralHeader(repositoryUri, $"x-ms-site-restricted-token:{token}");
                    repository.FetchWithoutConflict(repositoryUri, "master");
                    return repository;
                }
                else
                {
                    throw new NotSupportedException($"build Type {buildType} is not supported");
                }
            }
            catch (Exception e)
            {
                tracer.Step(nameof(PrepareRepositoryForBuildJob), new Dictionary<string, string> { { "Exception", e.Message }, { "Stack", e.StackTrace } });
                throw;
            }
        }

        private static ITracer GetTracer()
        {
            if (level > TraceLevel.Off)
            {
                var tracer = new XmlTracer(env.TracePath, level);
                string logFile = System.Environment.GetEnvironmentVariable(Constants.TraceFileEnvKey);
                if (!String.IsNullOrEmpty(logFile))
                {
                    // Kudu.exe is executed as part of git.exe (post-receive), giving its initial depth of 4 indentations
                    string logPath = Path.Combine(env.TracePath, logFile);
                    // since git push is "POST", which then run kudu.exe
                    return new CascadeTracer(tracer, new TextTracer(logPath, level, 4), new ETWTracer(env.RequestId, requestMethod: HttpMethod.Post.Method));
                }

                return tracer;
            }

            return NullTracer.Instance;
        }

        private static ILogger GetLogger()
        {
            if (level > TraceLevel.Off)
            {
                string logFile = System.Environment.GetEnvironmentVariable(Constants.TraceFileEnvKey);
                if (!String.IsNullOrEmpty(logFile))
                {
                    string logPath = Path.Combine(env.RootPath, Constants.DeploymentTracePath, logFile);
                    return new CascadeLogger(logger, new TextLogger(logPath));
                }
            }

            return logger;
        }

        private static string GetSettingsPath()
        {
            return Path.Combine(env.DeploymentsPath, Constants.DeploySettingsPath);
        }

        private static IEnvironment GetEnvironment(string siteRoot)
        {
            string root = Path.GetFullPath(Path.Combine(siteRoot, ".."));
            string appName = root.Replace("/home/apps/", "");
            var requestIdEnv = string.Format(Constants.RequestIdEnvFormat, appName);
            string requestId = System.Environment.GetEnvironmentVariable(requestIdEnv);

            // CORE TODO : test by setting SCM_REPOSITORY_PATH 
            // REVIEW: this looks wrong because it ignores SCM_REPOSITORY_PATH
            string repositoryPath = Path.Combine(siteRoot, Constants.RepositoryPath);

            // SCM_BIN_PATH is introduced in Kudu apache config file 
            // Provide a way to override Kudu bin path, to resolve issue where we can not find the right Kudu bin path when running on mono
            // CORE TODO I don't think this is needed anymore? This env var is not used anywhere but here.
            string binPath = System.Environment.GetEnvironmentVariable("SCM_BIN_PATH");
            if (string.IsNullOrWhiteSpace(binPath))
            {
                // CORE TODO Double check. Process.GetCurrentProcess() always gets the dotnet.exe process,
                // so changed to Assembly.GetEntryAssembly().Location
                binPath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            }

            // CORE TODO Handing in a null IHttpContextAccessor (and KuduConsoleFullPath) again
            var env = new Kudu.Core.Environment(root,
            EnvironmentHelper.NormalizeBinPath(binPath),
            repositoryPath,
            requestId,
            Path.Combine(AppContext.BaseDirectory, "KuduConsole", "kudu.dll"),
            null,
            appName);
            return env;
        }
    }
}