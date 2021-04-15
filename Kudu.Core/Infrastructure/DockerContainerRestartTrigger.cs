using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Kudu.Contracts.Deployment;
using Kudu.Core.Functions;
using Kudu.Core.Helpers;
using Kudu.Core.K8SE;
using Newtonsoft.Json;

namespace Kudu.Core.Infrastructure
{
    // Utility for touching the restart trigger file on Linux, which will restart the
    // site container.
    // Contents of the trigger file are irrelevant but this leaves a small explanation for
    // users who stumble on it.
    public static class DockerContainerRestartTrigger
    {
        private const string CONFIG_DIR_NAME = "config";
        private const string TRIGGER_FILENAME = "restartTrigger.txt";

        private static readonly string FILE_CONTENTS_FORMAT = String.Concat(
            "Modifying this file will trigger a restart of the app container.",
            System.Environment.NewLine, System.Environment.NewLine,
            "The last modification Kudu made to this file was at {0}, for the following reason: {1}.",
            System.Environment.NewLine);

        public static void RequestContainerRestart(IEnvironment environment, string reason, string repositoryUrl = null, string appSubPath = "")
        {
            if (K8SEDeploymentHelper.IsK8SEEnvironment())
            {
                string appName = environment.K8SEAppName;
                string buildNumber = environment.CurrId;
                var functionTriggers = KedaFunctionTriggerProvider.GetFunctionTriggers(repositoryUrl, appName);
                var buildMetadata = new BuildMetadata()
                {
                    AppName = appName,
                    BuildVersion = buildNumber,
                    AppSubPath = appSubPath
                };

                //Only for function apps functionTriggers will be non-null/non-empty
                if (functionTriggers?.Any() == true)
                {
                    K8SEDeploymentHelper.UpdateFunctionAppTriggers(appName, functionTriggers, buildMetadata);
                }
                else
                {
                    K8SEDeploymentHelper.UpdateBuildNumber(appName, buildMetadata);
                }

                return;
            }

            if (OSDetector.IsOnWindows() && !EnvironmentHelper.IsWindowsContainers())
            {
                throw new NotSupportedException("RequestContainerRestart is only supported on Linux and Windows Containers");
            }

            var restartTriggerPath = Path.Combine(environment.SiteRootPath, CONFIG_DIR_NAME, TRIGGER_FILENAME);

            FileSystemHelpers.CreateDirectory(Path.GetDirectoryName(restartTriggerPath));

            var fileContents = String.Format(
                FILE_CONTENTS_FORMAT,
                DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                reason);

            FileSystemHelpers.WriteAllText(restartTriggerPath, fileContents);
        }
    }
}
