﻿using Kudu.Contracts.Deployment;
using Kudu.Core;
using Kudu.Core.Deployment;
using Kudu.Core.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kudu.Services.Deployment
{
    static class OneDeployHelper
    {
        // Stacks supported by OneDeploy 
        public const string Tomcat = "TOMCAT";
        public const string JavaSE = "JAVA";
        public const string JBossEap = "JBOSSEAP";

        private const string StackEnvVarName = "WEBSITE_STACK";

        // All paths are relative to HOME directory
        public const string WwwrootDirectoryRelativePath = "site/wwwroot";
        public const string ScriptsDirectoryRelativePath = "site/scripts";
        public const string LibsDirectoryRelativePath = "site/libs";

        public static bool IsLegacyWarPathValid(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var segments = path.Split('/');

            return segments.Length == 2 && path.StartsWith("webapps/", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(segments[1]);
        }

        public static bool EnsureValidStack(ArtifactType artifactType, List<string> expectedStacks, bool ignoreStack, out string error)
        {
            var websiteStack = GetWebsiteStack();

            bool isStackValid = expectedStacks != null && expectedStacks.Any(stack => string.Equals(websiteStack, stack, StringComparison.OrdinalIgnoreCase));

            if (ignoreStack || isStackValid)
            {
                error = null;
                return true;
            }

            error = $"Artifact type = '{artifactType}' cannot be deployed to stack = '{websiteStack}'. " +
                    $"Site should be configured to run with stack = {string.Join(" or ", expectedStacks)}";
            return false;
        }

        public static bool EnsureValidPath(ArtifactType artifactType, string designatedDirectoryRelativePath, ref string path, out string error)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = $"Path must be defined for type='{artifactType}'";
                return false;
            }

            // Keep things simple by disallowing trailing slash
            if (path.EndsWith("/", StringComparison.Ordinal))
            {
                error = $"Path cannot end with a '/'";
                return false;
            }

            // If specified path is absolute, make sure it points to the designated directory for the artifact type
            if (path.StartsWith("/", StringComparison.Ordinal))
            {
                string designatedRootAbsolutePath = $"/home/{designatedDirectoryRelativePath}/";

                if (!path.StartsWith($"{designatedRootAbsolutePath}", StringComparison.Ordinal))
                {
                    error = $"Absolute path = '{path}' for artifact type = '{artifactType}' is invalid. " +
                            $"Either use a relative path or use an absolute path that start with prefix '{designatedRootAbsolutePath}'";
                    return false;
                }

                path = path.Substring(designatedRootAbsolutePath.Length);

                if (string.IsNullOrWhiteSpace(path))
                {
                    error = $"Absolute path for artifact type = '{artifactType}' should be of the form {designatedRootAbsolutePath}[directoryname/]<filename>";
                    return false;
                }
            }

            error = null;
            return true;
        }

        public static string GetWebsiteStack()
        {
            return System.Environment.GetEnvironmentVariable(StackEnvVarName);
        }

        public static string GetAbsolutePath(IEnvironment environment, string relativePath)
        {
            return Path.Combine(environment.RootPath, relativePath);
        }

        public static string GetStartupFileName()
        {
            return OSDetector.IsOnWindows() ? "startup.cmd" : "startup.sh";
        }

        // Extract directory path and file name from relativeFilePath
        // Example: path=a/b/c.jar => TargetSubDirectoryRelativePath=a/b and TargetFileName=c.jar
        // Example: path=c.jar => TargetSubDirectoryRelativePath=null and TargetFileName=c.jar
        // Example: path=/c.jar => TargetSubDirectoryRelativePath="" and TargetFileName=c.jar
        // Example: path=null => TargetSubDirectoryRelativePath=null and TargetFileName=null
        public static void SetTargetSubDirectoyAndFileNameFromRelativePath(DeploymentInfoBase deploymentInfo, string relativeFilePath)
        {
            if (relativeFilePath != null)
            {
                relativeFilePath = relativeFilePath.TrimStart('/');
            }

            deploymentInfo.TargetFileName = Path.GetFileName(relativeFilePath);
            deploymentInfo.TargetSubDirectoryRelativePath = Path.GetDirectoryName(relativeFilePath);
        }
    }
}
