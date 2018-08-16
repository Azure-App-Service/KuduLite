﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Linq;
using Kudu.Contracts.Jobs;
using Kudu.Contracts.Settings;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace Kudu.Core.Jobs
{
    public abstract class JobsManagerBase
    {
        protected static readonly IScriptHost[] ScriptHosts = new IScriptHost[]
        {
            new WindowsScriptHost(),
            new PowerShellScriptHost(),
            new BashScriptHost(),
            new PythonScriptHost(),
            new PhpScriptHost(),
            new NodeScriptHost(),
            new DnxScriptHost(),
            new FSharpScriptHost()
        };

        public static bool IsUsingSdk(string specificJobDataPath)
        {
            try
            {
                string webJobsSdkMarkerFilePath = Path.Combine(specificJobDataPath, "webjobssdk.marker");
                return FileSystemHelpers.FileExists(webJobsSdkMarkerFilePath);
            }
            catch
            {
                return false;
            }
        }
    }

    // CORE TODO This used to implement IRegisteredObject and called HostingEnvironment.UnregisterObject(this) in Stop().
    // See https://stackoverflow.com/a/42785667/173303. I have removed the references to the offending classes since they
    // are not in ASP.NET core but we still nee to call Stop() in the Register() callback shown in the StackOverflow answer.
    public abstract class JobsManagerBase<TJob> : JobsManagerBase, IJobsManager<TJob>, IDisposable where TJob : JobBase, new()
    {
        private const string DefaultScriptFileName = "run";

        private readonly string _jobsTypePath;

        private string _lastKnownAppBaseUrlPrefix;

        private readonly IHttpContextAccessor _httpContextAccessor;

        internal static object jobsListCacheLockObj = new object();

        protected IEnvironment Environment { get; private set; }

        protected IDeploymentSettingsManager Settings { get; private set; }

        protected ITraceFactory TraceFactory { get; private set; }

        public string JobsBinariesPath { get; private set; }

        protected string JobsDataPath { get; private set; }

        protected IAnalytics Analytics { get; private set; }

        protected JobsFileWatcher JobsWatcher { get; set; }
        internal static IEnumerable<TJob> JobListCache { get; set; }

        private DateTime _jobListCacheExpiryDate;

        private const int _jobListCacheTimeOutInMinutes = 10;

        List<Action<string>> FileWatcherExtraEventHandlers;

        protected JobsManagerBase(
            ITraceFactory traceFactory,
            IEnvironment environment,
            IDeploymentSettingsManager settings,
            IAnalytics analytics,
            IHttpContextAccessor httpContextAccessor,
            string jobsTypePath)
        {
            TraceFactory = traceFactory;
            Environment = environment;
            Settings = settings;
            Analytics = analytics;

            _httpContextAccessor = httpContextAccessor;

            _jobsTypePath = jobsTypePath;

            JobsBinariesPath = Path.Combine(Environment.JobsBinariesPath, jobsTypePath);
            JobsDataPath = Path.Combine(Environment.JobsDataPath, jobsTypePath);
            JobsWatcher = new JobsFileWatcher(JobsBinariesPath, OnJobChanged, null, ListJobNames, traceFactory, analytics, jobsTypePath);
        }

        protected virtual IEnumerable<string> ListJobNames(bool forceRefreshCache)
        {
            return ListJobs(forceRefreshCache).Select(job => job.Name);
        }

        public void RegisterExtraEventHandlerForFileChange(Action<string> action)
        {
            if (FileWatcherExtraEventHandlers == null)
            {
                FileWatcherExtraEventHandlers = new List<Action<string>>();
            }
            FileWatcherExtraEventHandlers.Add(action);
        }

        private void OnJobChanged(string jobName)
        {
            ClearJobListCache();
            if (FileWatcherExtraEventHandlers != null)
            {
                foreach (Action<string> action in FileWatcherExtraEventHandlers)
                {
                    action.Invoke(jobName);
                }
            }

        }

        public IEnumerable<TJob> ListJobs(bool forceRefreshCache)
        {
            var jobList = JobListCache;
            lock (jobsListCacheLockObj)
            {
                if (jobList == null || forceRefreshCache || _jobListCacheExpiryDate < DateTime.Now)
                {
                    jobList = ListJobsInternal();
                    JobListCache = jobList;
                    _jobListCacheExpiryDate = DateTime.Now.AddMinutes(_jobListCacheTimeOutInMinutes);
                }
            }
            return jobList;
        }

        internal static void ClearJobListCache()
        {
            JobListCache = null;
        }

        public abstract TJob GetJob(string jobName);

        public TJob CreateOrReplaceJobFromZipStream(Stream zipStream, string jobName)
        {
            return CreateOrReplaceJob(jobName,
                (jobDirectory) =>
                {
                    using (var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                    {
                        zipArchive.Extract(jobDirectory.FullName);
                    }
                });
        }

        public TJob CreateOrReplaceJobFromFileStream(Stream scriptFileStream, string jobName, string scriptFileName)
        {
            return CreateOrReplaceJob(jobName,
                (jobDirectory) =>
                {
                    string filePath = Path.Combine(jobDirectory.FullName, scriptFileName);
                    using (Stream destinationFileStream = FileSystemHelpers.OpenFile(filePath, FileMode.Create))
                    {
                        scriptFileStream.CopyTo(destinationFileStream);
                    }
                });
        }

        private TJob CreateOrReplaceJob(string jobName, Action<DirectoryInfoBase> writeJob)
        {
            DirectoryInfoBase jobDirectory = GetJobDirectory(jobName);
            if (jobDirectory.Exists)
            {
                // If job binaries already exist, remove them to make place for new job binaries
                OperationManager.Attempt(
                    () => FileSystemHelpers.DeleteDirectorySafe(jobDirectory.FullName, ignoreErrors: false));
            }

            jobDirectory.Create();
            jobDirectory = GetJobDirectory(jobName); // regenerating the DirectoryInfoBase instance to populate the Exists method with true.

            writeJob(jobDirectory);

            return BuildJob(jobDirectory, nullJobOnError: false);
        }

        public void DeleteJob(string jobName)
        {
            try
            {
                var jobDirectory = GetJobDirectory(jobName);
                if (!jobDirectory.Exists)
                {
                    return;
                }

                var jobsSpecificDataPath = GetSpecificJobDataPath(jobName);

                // Remove both job binaries and data directories
                OperationManager.Attempt(() =>
                {
                    FileSystemHelpers.DeleteDirectorySafe(jobDirectory.FullName, ignoreErrors: false);
                    FileSystemHelpers.DeleteDirectorySafe(jobsSpecificDataPath, ignoreErrors: false);
                }, retries: 3, delayBeforeRetry: 2000);

                ClearJobListCache();
            }
            catch (Exception ex)
            {
                // Ignore failure to remove job here
                TraceFactory.GetTracer().TraceError(ex.ToString());
            }
        }

        public void CleanupDeletedJobs()
        {
            IEnumerable<TJob> jobs = ListJobs(forceRefreshCache: true);
            IEnumerable<string> jobNames = jobs.Select(j => j.Name);
            DirectoryInfoBase jobsDataDirectory = FileSystemHelpers.DirectoryInfoFromDirectoryName(JobsDataPath);
            if (jobsDataDirectory.Exists)
            {
                DirectoryInfoBase[] jobDataDirectories = jobsDataDirectory.GetDirectories("*", SearchOption.TopDirectoryOnly);
                IEnumerable<string> allJobDataDirectories = jobDataDirectories.Select(j => j.Name);
                IEnumerable<string> directoriesToRemove = allJobDataDirectories.Except(jobNames, StringComparer.OrdinalIgnoreCase);
                foreach (string directoryToRemove in directoriesToRemove)
                {
                    var tracer = TraceFactory.GetTracer();
                    using (tracer.Step("CleanupDeletedJobs"))
                    {
                        tracer.Trace("Removed job data path as the job was already deleted: " + directoryToRemove);
                        FileSystemHelpers.DeleteDirectorySafe(Path.Combine(JobsDataPath, directoryToRemove));
                    }
                }
            }
        }

        private string GetSpecificJobDataPath(string jobName)
        {
            return Path.Combine(JobsDataPath, jobName);
        }

        protected TJob GetJobInternal(string jobName)
        {
            DirectoryInfoBase jobDirectory = GetJobDirectory(jobName);
            return BuildJob(jobDirectory);
        }

        protected IEnumerable<TJob> ListJobsInternal()
        {
            var jobs = new List<TJob>();

            IEnumerable<DirectoryInfoBase> jobDirectories = ListJobDirectories(JobsBinariesPath);
            foreach (DirectoryInfoBase jobDirectory in jobDirectories)
            {
                TJob job = BuildJob(jobDirectory);
                if (job != null)
                {
                    jobs.Add(job);
                }
            }

            return jobs;
        }

        protected TJob BuildJob(DirectoryInfoBase jobDirectory, bool nullJobOnError = true)
        {
            try
            {
                if (!jobDirectory.Exists)
                {
                    return null;
                }

                DirectoryInfoBase jobScriptDirectory = GetJobScriptDirectory(jobDirectory);

                string jobName = jobDirectory.Name;
                FileInfoBase[] files = jobScriptDirectory.GetFiles("*.*", SearchOption.TopDirectoryOnly);
                IScriptHost scriptHost;
                string scriptFilePath = FindCommandToRun(files, out scriptHost);

                if (scriptFilePath == null)
                {
                    // Return a job representing an error for no runnable script file found for job
                    if (nullJobOnError)
                    {
                        return null;
                    }

                    return new TJob
                    {
                        Name = jobName,
                        JobType = _jobsTypePath,
                        Error = Resources.Error_NoRunnableScriptForJob,
                    };
                }

                string runCommand = scriptFilePath.Substring(jobDirectory.FullName.Length + 1);

                var job = new TJob
                {
                    Name = jobName,
                    Url = BuildJobsUrl(jobName),
                    ExtraInfoUrl = BuildExtraInfoUrl(jobName),
                    ScriptFilePath = scriptFilePath,
                    RunCommand = runCommand,
                    JobType = _jobsTypePath,
                    ScriptHost = scriptHost,
                    UsingSdk = IsUsingSdk(GetSpecificJobDataPath(jobName)),
                    JobBinariesRootPath = jobScriptDirectory.FullName,
                    Settings = GetJobSettings(jobName)
                };

                UpdateJob(job);

                return job;
            }
            catch (Exception ex)
            {
                Analytics.UnexpectedException(ex);

                // Return a job representing an error for no runnable script file found for job
                if (nullJobOnError)
                {
                    if (ex is IOException)
                    {
                        // unexpected IOException given simply reading files/folders.
                        // just rethrow and let the upper layers handle it.
                        throw;
                    }

                    return null;
                }

                return new TJob
                {
                    JobType = _jobsTypePath,
                    Error = ex.Message,
                };
            }
        }

        /// <summary>
        /// Deploy (external) jobs from {sourcePath} by moving them over to the main jobs directory (JobsBinariesPath)
        /// These external jobs usually come from site extensions
        /// </summary>
        public void SyncExternalJobs(string sourcePath, string sourceName)
        {
            sourcePath = Path.Combine(sourcePath, "App_Data\\jobs", _jobsTypePath);

            CleanupExternalJobs(sourceName);

            // Move jobs from source path
            IEnumerable<DirectoryInfoBase> sourceJobDirectories = ListJobDirectories(sourcePath);
            foreach (DirectoryInfoBase sourceJobDirectory in sourceJobDirectories)
            {
                // Check whether job was already copied by checking existence of file job.copied
                string jobPath = sourceJobDirectory.FullName;
                MoveExternalJob(jobPath, sourceName);
            }
        }

        public void CleanupExternalJobs(string sourceName)
        {
            // Find all jobs for the source name provided
            // Job name will look like: {source name}({job name}) for example: "daas(sitepinger)"
            IEnumerable<DirectoryInfoBase> jobDirectories = ListJobDirectories(JobsBinariesPath, sourceName + "(*)");
            foreach (DirectoryInfoBase jobDirectory in jobDirectories)
            {
                DeleteJob(jobDirectory.Name);
            }
        }

        private void MoveExternalJob(string sourcePath, string sourceName)
        {
            string jobName = "{0}({1})".FormatInvariant(sourceName, Path.GetFileName(sourcePath));
            string toPath = Path.Combine(JobsBinariesPath, jobName);
            FileSystemHelpers.DeleteDirectorySafe(toPath);
            FileSystemHelpers.EnsureDirectory(Path.GetDirectoryName(toPath));
            Directory.Move(sourcePath, toPath);
        }

        public JobSettings GetJobSettings(string jobName)
        {
            JobSettings jobSettings;

            try
            {
                jobSettings = OperationManager.Attempt(() =>
                {
                    var jobDirectory = GetJobBinariesDirectory(jobName);

                    var jobSettingsPath = GetJobSettingsPath(jobDirectory);
                    if (!FileSystemHelpers.FileExists(jobSettingsPath))
                    {
                        return null;
                    }

                    string jobSettingsContent = FileSystemHelpers.ReadAllTextFromFile(jobSettingsPath);
                    return JsonConvert.DeserializeObject<JobSettings>(jobSettingsContent);
                });
            }
            catch (Exception ex)
            {
                TraceFactory.GetTracer().TraceError(ex.ToString());
                jobSettings = null;
            }

            return jobSettings ?? new JobSettings();
        }

        public void SetJobSettings(string jobName, JobSettings jobSettings)
        {
            var jobDirectory = GetJobBinariesDirectory(jobName);

            var jobSettingsPath = GetJobSettingsPath(jobDirectory);
            string jobSettingsContent = JsonConvert.SerializeObject(jobSettings);
            FileSystemHelpers.WriteAllTextToFile(jobSettingsPath, jobSettingsContent);
        }

        public void Stop(bool immediate)
        {
            if (IsShuttingdown)
            {
                return;
            }

            IsShuttingdown = true;
            OnShutdown();
        }

        protected abstract void OnShutdown();

        protected bool IsShuttingdown { get; private set; }

        private static string GetJobSettingsPath(DirectoryInfoBase jobDirectory)
        {
            return Path.Combine(jobDirectory.FullName, JobSettings.JobSettingsFileName);
        }

        protected abstract void UpdateJob(TJob job);

        protected TJobStatus GetStatus<TJobStatus>(string statusFilePath) where TJobStatus : class, IJobStatus, new()
        {
            return JobLogger.ReadJobStatusFromFile<TJobStatus>(Analytics, statusFilePath);
        }

        protected Uri BuildJobsUrl(string relativeUrl)
        {
            if (AppBaseUrlPrefix == null)
            {
                return null;
            }

            return new Uri("{0}/api/{1}webjobs/{2}".FormatInvariant(AppBaseUrlPrefix, _jobsTypePath, relativeUrl));
        }

        protected Uri BuildVfsUrl(string relativeUrl)
        {
            if (AppBaseUrlPrefix == null)
            {
                return null;
            }

            return new Uri("{0}/vfs/data/jobs/{1}/{2}".FormatInvariant(AppBaseUrlPrefix, _jobsTypePath, relativeUrl));
        }

        private Uri BuildExtraInfoUrl(string jobName)
        {
            if (AppBaseUrlPrefix == null)
            {
                return null;
            }

            return new Uri("{0}/azurejobs/#/jobs/{1}/{2}".FormatInvariant(AppBaseUrlPrefix, _jobsTypePath, jobName));
        }

        protected string AppBaseUrlPrefix
        {
            get
            {
                var context = _httpContextAccessor.HttpContext;

                if (context == null)
                {
                    return _lastKnownAppBaseUrlPrefix;
                }

                var requestUrl = new Uri(context.Request.GetDisplayUrl());

                _lastKnownAppBaseUrlPrefix = requestUrl.GetLeftPart(UriPartial.Authority);
                return _lastKnownAppBaseUrlPrefix;
            }
        }

        private static IEnumerable<DirectoryInfoBase> ListJobDirectories(string path, string searchPattern = "*")
        {
            if (!FileSystemHelpers.DirectoryExists(path))
            {
                return Enumerable.Empty<DirectoryInfoBase>();
            }

            DirectoryInfoBase jobsDirectory = FileSystemHelpers.DirectoryInfoFromDirectoryName(path);
            return jobsDirectory.GetDirectories(searchPattern, SearchOption.TopDirectoryOnly);
        }

        private DirectoryInfoBase GetJobDirectory(string jobName)
        {
            string jobPath = Path.Combine(JobsBinariesPath, jobName);
            return FileSystemHelpers.DirectoryInfoFromDirectoryName(jobPath);
        }

        private DirectoryInfoBase GetJobBinariesDirectory(string jobName)
        {
            DirectoryInfoBase jobDirectory = GetJobDirectory(jobName);
            if (!jobDirectory.Exists)
            {
                throw new JobNotFoundException($"Cannot find '{jobDirectory.FullName}' directory for '{jobName}' triggered job");
            }

            return GetJobScriptDirectory(jobDirectory);
        }

        private DirectoryInfoBase GetJobScriptDirectory(DirectoryInfoBase jobDirectory)
        {
            // Return the directory where the script should be found using the following logic:
            // If current directory (jobDirectory) has only one sub-directory and no files recurse this using that sub-directory
            // Otherwise return current directory
            if (jobDirectory != null && jobDirectory.Exists)
            {
                var jobFiles = jobDirectory.GetFileSystemInfos();
                if (jobFiles.Length == 1 && jobFiles[0] is DirectoryInfoBase)
                {
                    return GetJobScriptDirectory(jobFiles[0] as DirectoryInfoBase);
                }
            }

            return jobDirectory;
        }

        private static string FindCommandToRun(FileInfoBase[] files, out IScriptHost scriptHostFound)
        {
            string secondaryScriptFound = null;

            scriptHostFound = null;

            foreach (IScriptHost scriptHost in ScriptHosts)
            {
                if (String.IsNullOrEmpty(scriptHost.HostPath))
                {
                    continue;
                }

                foreach (string supportedExtension in scriptHost.SupportedExtensions)
                {
                    var supportedFiles = files.Where(f => String.Equals(f.Extension, supportedExtension, StringComparison.OrdinalIgnoreCase));
                    if (supportedFiles.Any())
                    {
                        var scriptFound =
                            supportedFiles.FirstOrDefault(f => String.Equals(f.Name, DefaultScriptFileName + supportedExtension, StringComparison.OrdinalIgnoreCase));

                        if (scriptFound != null)
                        {
                            scriptHostFound = scriptHost;
                            return scriptFound.FullName;
                        }

                        if (secondaryScriptFound == null)
                        {
                            scriptHostFound = scriptHost;
                            secondaryScriptFound = supportedFiles.First().FullName;
                        }
                    }
                }

                foreach (var supportedFileName in scriptHost.SupportedFileNames)
                {
                    var supportedFiles = files.Where(f => String.Equals(f.Name, supportedFileName, StringComparison.OrdinalIgnoreCase));
                    if (supportedFiles.Any())
                    {
                        scriptHostFound = scriptHost;
                        return supportedFiles.First().FullName;
                    }
                }
            }

            if (secondaryScriptFound != null)
            {
                return secondaryScriptFound;
            }

            return null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            // HACK: Next if statement should be removed once ninject wlll not dispose this class
            // Since ninject automatically calls dispose we currently disable it
            if (disposing)
            {
                return;
            }
            // End of code to be removed

            if (disposing)
            {
                if (JobsWatcher != null)
                {
                    JobsWatcher.Dispose();
                    JobsWatcher = null;
                }
            }
        }
    }
}
