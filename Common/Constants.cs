using System;
using System.IO;

namespace Kudu
{
    public static class Constants
    {
        //Scan functionality files
        public const string ScanLockFile = "scan.lock";
        public static string ScanStatusFile = "status.json";
        public static string ScanLogFile = "scan_log.log";
        public static string ScanFolderName = "Scan_";
        public static string MaxScans = "2";
        public static string ScanDir = "/home/site/wwwroot";
        public static string ScriptPath = "/custom_scripts/daily_scan_script.sh";
        public static string ScanCommand = ScriptPath+" "+ScanDir;
        public static string ScanTimeOutMillSec = "1200000";    // 20 mins
        public static string ScanManifest = "modified_times.json";
        public static string AggregrateScanResults = "aggregrate_scans.log";
        public static string TempScanFile = "temp_scan_monitor";

        public const string WebRoot = "wwwroot";
        public const string MappedSite = "/_app";
        public const string RepositoryPath = "repository";
        public const string ZipTempPath = "zipdeploy";
        public const string ZipExtractPath = "extracted";

        public const string LockPath = "locks";
        public const string DeploymentLockFile = "deployments.lock";
        public const string StatusLockFile = "status.lock";
        public const string SSHKeyLockFile = "sshkey.lock";
        public const string HooksLockFile = "hooks.lock";
        public const string StatusLockName = "status";
        public const string SshLockName = "ssh";
        public const string HooksLockName = "hooks";
        public const string DeploymentLockName = "deployment";
        public const string SSHKeyPath = ".ssh";
        public const string NpmDebugLogFile = "npm-debug.log";

        public const string DeploymentCachePath = "deployments";
        public const string SiteExtensionsCachePath = "siteextensions";
        public const string DeploymentToolsPath = "tools";
        public const string SiteFolder = @"site";
        public const string LogFilesPath = @"LogFiles";
        public const string ApplicationLogFilesDirectory = "Application";
        public static readonly string TracePath = Path.Combine(LogFilesPath, "kudu", "trace");
        public const string SiteExtensionLogsDirectory = "siteExtLogs";
        public const string DeploySettingsPath = "settings.xml";
        public const string ActiveDeploymentFile = "active";
        public const string ScriptsPath = "Scripts";
        public const string NodeModulesPath = "node_modules";
        public const string FirstDeploymentManifestFileName = "firstDeploymentManifest";
        public const string ManifestFileName = "manifest";

        public const string AppDataPath = "App_Data";
        public const string DataPath = "data";
        public const string JobsPath = "jobs";
        public const string ContinuousPath = "continuous";
        public const string TriggeredPath = "triggered";

        public const string DummyRazorExtension = ".kudu777";

        // Kudu trace text file related
        public const string DeploymentTracePath = LogFilesPath + @"/kudu/deployment";

        public const string TraceFileFormat = "{0}-{1}.txt";
        public const string TraceFileEnvKey = "KUDU_TRACE_FILE";

        public const string DiagnosticsPath = @"diagnostics";
        public const string LocksPath = @"locks";
        public const string SettingsJsonFile = @"settings.json";

        public const string HostingStartHtml = "hostingstart.html";

        public const string DnxDefaultVersion = "1.0.0-rc1-final";
        public const string DnxDefaultClr = "CLR";

        // These should match the ones that are set by Azure
        public const string X64Bit = "AMD64";
        public const string X86Bit = "x86";

        public const string LatestDeployment = "latest";

        private static readonly TimeSpan _maxAllowedExectionTime = TimeSpan.FromMinutes(30);

        public static TimeSpan MaxAllowedExecutionTime
        {
            get { return _maxAllowedExectionTime; }
        }

        public const string ApplicationHostXdtFileName = "applicationHost.xdt";
        public const string ScmApplicationHostXdtFileName = "scmApplicationHost.xdt";

        public const string ArrLogIdHeader = "x-arr-log-id";
        public const string RequestIdHeader = "x-ms-request-id";
        public const string ClientRequestIdHeader = "x-ms-client-request-id";
        public const string RequestDateTimeUtc = "RequestDateTimeUtc";

        public const string SiteOperationHeaderKey = "X-MS-SITE-OPERATION";
        public const string SiteOperationRestart = "restart";

        public const string LogicAppJson = "logicapp.json";
        public const string LogicAppUrlKey = "LOGICAPP_URL";

        public const string AppSettingsRegex = "%.*?%";

        public const string SiteExtensionProvisioningStateCreated = "Created";
        public const string SiteExtensionProvisioningStateAccepted = "Accepted";
        public const string SiteExtensionProvisioningStateSucceeded = "Succeeded";
        public const string SiteExtensionProvisioningStateFailed = "Failed";
        public const string SiteExtensionProvisioningStateCanceled = "Canceled";

        public const string SiteExtensionOperationInstall = "install";

        // TODO: need localization?
        public const string SiteExtensionProvisioningStateNotFoundMessageFormat = "'{0}' not found.";

        public const string FreeSKU = "Free";
        public const string BasicSKU = "Basic";

        //Setting for VC++ for node builds
        public const string VCVersion = "2015";

        public const string SiteRestrictedToken = "x-ms-site-restricted-token";
        public const string SiteAuthEncryptionKey = "WEBSITE_AUTH_ENCRYPTION_KEY";
        public const string HttpHost = "HTTP_HOST";
        public const string WebSiteSwapSlotName = "WEBSITE_SWAP_SLOTNAME";
        public const string AzureWebsiteInstanceId = "WEBSITE_INSTANCE_ID";
        public const string ContainerName = "CONTAINER_NAME";

        public const string Function = "function";
        public const string Functions = "functions";
        public const string FunctionsConfigFile = "function.json";
        public const string FunctionsHostConfigFile = "host.json";
        public const string ProxyConfigFile = "proxies.json";
        public const string Secrets = "secrets";
        public const string SampleData = "sampledata";
        public const string FunctionsPortal = "FunctionsPortal";
        public const string FunctionKeyNewFormat = "~0.7";
        public const string FunctionRunTimeVersion = "FUNCTIONS_EXTENSION_VERSION";
        public const string ScmRunFromPackage = "SCM_RUN_FROM_PACKAGE";
        public const string WebSiteSku = "WEBSITE_SKU";
        public const string WebSiteElasticScaleEnabled = "WEBSITE_ELASTIC_SCALING_ENABLED";
        public const string DynamicSku = "Dynamic";
        public const string ElasticScaleEnabled = "1";
        public const string AzureWebJobsSecretStorageType = "AzureWebJobsSecretStorageType";
        public const string HubName = "HubName";
        public const string DurableTaskStorageConnection = "connection";
        public const string DurableTaskStorageConnectionName = "azureStorageConnectionStringName";
        public const string DurableTaskSqlConnectionName = "connectionStringName";
        public const string DurableTaskStorageProvider = "storageProvider";
        public const string DurableTaskMicrosoftSqlProviderType = "mssql";
        public const string MicrosoftSqlScaler = "mssql";
        public const string AzureQueueScaler = "azure-queue";
        public const string DurableTask = "durableTask";
        public const string WorkflowAppKind = "workflowApp";
        public const string WorkflowExtensionName = "workflow";
        public const string WorkflowSettingsName = "Settings";
        public const string Extensions = "extensions";
        public const string SitePackages = "SitePackages";
        public const string PackageNameTxt = "packagename.txt";
        public const string KuduBuild = "1.0.0.7";

        public const string WebSSHReverseProxyPortEnvVar = "KUDU_WEBSSH_PORT";
        public const string WebSSHReverseProxyDefaultPort = "3000";

        public const string LinuxLogEventStreamName = "MS_KUDU_LOGS";
        public const string WebSiteHomeStampName = "WEBSITE_HOME_STAMPNAME";
        public const string WebSiteStampDeploymentId = "WEBSITE_STAMP_DEPLOYMENT_ID";
        public const string IsK8SEEnvironment = "K8SE_BUILD_SERVICE";

        public const string OneDeploy = "OneDeploy";
        public const string ArtifactStagingDirectoryName = "extracted";

        public const string K8SEAppTypeDefault = "functionapp,kubernetes,linux";

        public const string IsBuildJob = "IS_BUILD_JOB"; //this is to indicate the current pod is build job pod.
        public const string UseBuildJob = "USE_BUILD_JOB"; // this is an EC to control whether to use build job. used by build service.
    }
}
