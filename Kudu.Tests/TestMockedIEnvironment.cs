using Kudu.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Tests
{
    public class TestMockedIEnvironment : IEnvironment
    {
        public string _RootPath = "/";
        public string _SiteRootPath = "/site";
        public string _RepositoryPath = "/site/repository";
        public string _WebRootPath = "/site/wwwroot";
        public string _DeploymentsPath = "/site/deployments";
        public string _artifactsPath = "/site/artifacts";
        public string _DeploymentToolsPath = "/site/deployments/tools";
        public string _SiteExtensionSettingsPath = "/site/siteextensions";
        public string _DiagnosticsPath = "/site/diagnostics";
        public string _LocksPath = "/site/locks";
        public string _SshKeyPath = "/.ssh";
        public string _TempPath = "/tmp";
        public string _ZipTempPath = "/tmp/zipdeploy";
        public string _ScriptPath = "/site/scripts";
        public string _NodeModulesPath = "/site/node_modules";
        public string _LogFilesPath = "/logfiles";
        public string _ApplicationLogFilesPath = "/logfiles/application";
        public string _TracePath = "/logfiles/kudu/trace";
        public string _AnalyticsPath = "/site/siteExtLogs";
        public string _DeploymentTracePath = "/logfiles/kudu/deployment";
        public string _DataPath = "/data";
        public string _JobsDataPath = "/data/jobs";
        public string _JobsBinariesPath = "/site/wwwroot/app_data/jobs";
        public string _SecondaryJobsBinariesPath = "/site/jobs";
        public string _FunctionsPath = "/site/wwwroot";
        public string _AppBaseUrlPrefix = "siteName.azurewebsites.net";
        public string _RequestId = "00000000-0000-0000-0000-000000000000";
        public string _KuduConsoleFullPath = "KuduConsole/kudu.dll";
        public string _SitePackagesPath = "/data/SitePackages";
        public bool _IsOnLinuxConsumption = false;

        public string RootPath => _RootPath;
        public string SiteRootPath => _SiteRootPath;
        public string RepositoryPath { get => _RepositoryPath; set => _RepositoryPath = value; }
        public string WebRootPath => _WebRootPath;
        public string DeploymentsPath => _DeploymentsPath;
        public string ArtifactsPath => _artifactsPath;
        public string DeploymentToolsPath => _DeploymentToolsPath;
        public string SiteExtensionSettingsPath => _SiteExtensionSettingsPath;
        public string DiagnosticsPath => _DiagnosticsPath;
        public string LocksPath => _LocksPath;
        public string SSHKeyPath => _SshKeyPath;
        public string TempPath => _TempPath;
        public string ZipTempPath => _ZipTempPath;
        public string ScriptPath => _ScriptPath;
        public string NodeModulesPath => _NodeModulesPath;
        public string LogFilesPath => _LogFilesPath;
        public string ApplicationLogFilesPath => _ApplicationLogFilesPath;
        public string TracePath => _TracePath;
        public string AnalyticsPath => _AnalyticsPath;
        public string DeploymentTracePath => _DeploymentTracePath;
        public string DataPath => _DataPath;
        public string JobsDataPath => _JobsDataPath;
        public string JobsBinariesPath => _JobsBinariesPath;
        public string SecondaryJobsBinariesPath => _SecondaryJobsBinariesPath;
        public string FunctionsPath => _FunctionsPath;
        public string AppBaseUrlPrefix => _AppBaseUrlPrefix;
        public string RequestId => _RequestId;
        public string KuduConsoleFullPath => _KuduConsoleFullPath;
        public string SitePackagesPath => _SitePackagesPath;
        public bool IsOnLinuxConsumption => _IsOnLinuxConsumption;
    }
}
