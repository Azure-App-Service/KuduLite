using Kudu.Contracts.Settings;
using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Deployment.Oryx;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Kudu.Core.Helpers
{
    public class LinuxConsumptionDeploymentHelper
    {
        /// <summary>
        /// Specifically used for Linux Consumption to support Server Side build scenario
        /// </summary>
        /// <param name="context"></param>
        public static async Task SetupLinuxConsumptionFunctionAppDeployment(
            IEnvironment env,
            IDeploymentSettingsManager settings,
            DeploymentContext context,
            bool shouldSyncTriggers,
            bool shouldUpdateWebsiteRunFromPackage)
        {
            string builtFolder = context.OutputPath;
            string packageFolder = env.ArtifactsPath;
            string packageFileName = OryxBuildConstants.FunctionAppBuildSettings.LinuxConsumptionArtifactName;

            // Indicate the memory limitation on Linux Consumption
            context.Logger.Log($"Linux Consumption plan has a 1.5 GB memory limit on a remote build container.");
            context.Logger.Log($"To check our service limit, please visit https://docs.microsoft.com/en-us/azure/azure-functions/functions-scale#service-limits");

            // Try create a placeholder blob in AzureWebJobsStorage
            CloudBlockBlob blob = await GetPlaceholderBlob(context, settings, shouldUpdateWebsiteRunFromPackage);

            // Package built content from oryx build artifact
            string filePath = PackageArtifactFromFolder(env, settings, context, builtFolder, packageFolder, packageFileName);

            // Log function app dependencies to kusto (requirements.txt, package.json, .csproj)
            await LogDependenciesFile(context.RepositoryPath);

            // Upload from DeploymentsPath
            await UploadLinuxConsumptionFunctionAppBuiltContent(context, blob, filePath);

            // Clean up local built content
            FileSystemHelpers.DeleteDirectoryContentsSafe(context.OutputPath);

            // Change WEBSITE_RUN_FROM_PACKAGE app setting
            if (shouldUpdateWebsiteRunFromPackage)
            {
                await UpdateWebsiteRunFromPackageAppSetting(context, blob);
            }

            // Remove Linux consumption plan functionapp workers for the site
            await RemoveLinuxConsumptionFunctionAppWorkers(context);

            // Invoke a warmup call to the main function site
            if (shouldSyncTriggers)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await FunctionHostSyncTriggersAsync(context);
            }
        }

        private static async Task LogDependenciesFile(string builtFolder)
        {
            try
            {
                await PrintRequirementsTxtDependenciesAsync(builtFolder);
                await PrintPackageJsonDependenciesAsync(builtFolder);
                PrintCsprojDependenciesAsync(builtFolder);
            } catch (Exception)
            {
                KuduEventGenerator.Log().GenericEvent(
                    ServerConfiguration.GetApplicationName(),
                    $"dependencies,failed to parse function app dependencies",
                    Guid.Empty.ToString(),
                    string.Empty,
                    string.Empty,
                    string.Empty);
            }
        }

        private static async Task PrintRequirementsTxtDependenciesAsync(string builtFolder)
        {
            string filename = "requirements.txt";
            string requirementsTxtPath = Path.Combine(builtFolder, filename);
            if (File.Exists(requirementsTxtPath))
            {
                string[] lines = await File.ReadAllLinesAsync(requirementsTxtPath);
                foreach (string line in lines)
                {
                    int separatorIndex;
                    if (line.IndexOf("==") >= 0)
                    {
                        separatorIndex = line.IndexOf("==");
                    } else if (line.IndexOf(">=") >= 0)
                    {
                        separatorIndex = line.IndexOf(">=");
                    } else if (line.IndexOf("<=") >= 0)
                    {
                        separatorIndex = line.IndexOf("<=");
                    } else if (line.IndexOf(">") >= 0)
                    {
                        separatorIndex = line.IndexOf(">");
                    } else if (line.IndexOf("<") >= 0)
                    {
                        separatorIndex = line.IndexOf("<");
                    } else
                    {
                        separatorIndex = line.Length;
                    }

                    string package = line.Substring(0, separatorIndex).Trim();
                    string version = line.Substring(separatorIndex).Trim();

                    KuduEventGenerator.Log().GenericEvent(
                        ServerConfiguration.GetApplicationName(),
                        $"dependencies,python,{filename},{package},{version}",
                        Guid.Empty.ToString(),
                        string.Empty,
                        string.Empty,
                        string.Empty);
                }
            }
        }

        private static async Task PrintPackageJsonDependenciesAsync(string builtFolder)
        {
            string filename = "package.json";
            string packageJsonPath = Path.Combine(builtFolder, filename);
            if (File.Exists(packageJsonPath))
            {
                string content = await File.ReadAllTextAsync(packageJsonPath);
                JObject jobj = JObject.Parse(content);
                if (jobj.ContainsKey("devDependencies"))
                {
                    Dictionary<string, string> dictObj = jobj["devDependencies"].ToObject<Dictionary<string, string>>();
                    foreach (string key in dictObj.Keys)
                    {
                        KuduEventGenerator.Log().GenericEvent(
                            ServerConfiguration.GetApplicationName(),
                            $"dependencies,node,{filename},{key},{dictObj[key]},devDependencies",
                            Guid.Empty.ToString(),
                            string.Empty,
                            string.Empty,
                            string.Empty);
                    }
                }

                if (jobj.ContainsKey("dependencies"))
                {
                    Dictionary<string, string> dictObj = jobj["dependencies"].ToObject<Dictionary<string, string>>();
                    foreach (string key in dictObj.Keys)
                    {
                        KuduEventGenerator.Log().GenericEvent(
                            ServerConfiguration.GetApplicationName(),
                            $"dependencies,node,{filename},{key},{dictObj[key]},dependencies",
                            Guid.Empty.ToString(),
                            string.Empty,
                            string.Empty,
                            string.Empty);
                    }
                }
            }
        }

        private static void PrintCsprojDependenciesAsync(string builtFolder)
        {
            foreach (string csprojPath in Directory.GetFiles(builtFolder, "*.csproj", SearchOption.TopDirectoryOnly))
            {
                string filename = Path.GetFileName(csprojPath);
                XElement purchaseOrder = XElement.Load(csprojPath);
                foreach (var itemGroup in purchaseOrder.Elements("ItemGroup"))
                {
                    foreach (var packageReference in itemGroup.Elements("PackageReference"))
                    {
                        string include = packageReference.Attribute("Include").Value;
                        string version = packageReference.Attribute("Version").Value;
                        KuduEventGenerator.Log().GenericEvent(
                            ServerConfiguration.GetApplicationName(),
                            $"dependencies,dotnet,{filename},{include},{version}",
                            Guid.Empty.ToString(),
                            string.Empty,
                            string.Empty,
                            string.Empty);
                    }
                }
            }
        }

        private static async Task UploadLinuxConsumptionFunctionAppBuiltContent(DeploymentContext context, CloudBlockBlob blob, string filePath)
        {
            context.Logger.Log($"Uploading built content {filePath} for linux consumption function app...");

            try
            {
                await blob.UploadFromFileAsync(filePath);
            }
            catch (StorageException se)
            {
                context.Logger.Log($"Failed to upload because Azure Storage responds {se.RequestInformation.HttpStatusCode}.");
                context.Logger.Log(se.Message);
                throw new DeploymentFailedException(se);
            }
        }

        private static async Task<CloudBlockBlob> GetPlaceholderBlob(DeploymentContext context, IDeploymentSettingsManager settings, bool isWebsiteRunFromPackage)
        {
            context.Logger.Log($"Creating placeholder blob for linux consumption function app...");

            // Get or create blob
            if (isWebsiteRunFromPackage)
            {
                return await GetWebsiteRunFromPackageBlob(context, settings);

            } else
            {
                return GetScmRunFromPackageBlob(context, settings);
            }
        }

        private static async Task<CloudBlockBlob> GetWebsiteRunFromPackageBlob(
            DeploymentContext context, IDeploymentSettingsManager settings)
        {
            string azureWebJobsStorage = settings.GetValue(Constants.AzureWebJobsStorage);
            if (string.IsNullOrEmpty(azureWebJobsStorage))
            {
                azureWebJobsStorage = System.Environment.GetEnvironmentVariable(Constants.AzureWebJobsStorage);
            }

            // Check if storage account is an empty string
            if (string.IsNullOrEmpty(azureWebJobsStorage))
            {
                context.Logger.Log($"{Constants.AzureWebJobsStorage} should not be empty");
                throw new DeploymentFailedException(new ArgumentNullException(Constants.AzureWebJobsStorage));
            }

            CloudStorageAccount account;
            try
            {
                account = CloudStorageAccount.Parse(azureWebJobsStorage);
            }
            catch (Exception ex)
            {
                context.Logger.Log($"Failed to parse {Constants.AzureWebJobsStorage} when connecting to storage account");
                throw new DeploymentFailedException(ex);
            }

            // Get or create container
            CloudBlobClient service = account.CreateCloudBlobClient();
            CloudBlobContainer container = service.GetContainerReference(Constants.ScmRunFromPackageContainerName);
            try
            {
                await container.CreateIfNotExistsAsync();
            }
            catch (Exception ex)
            {
                context.Logger.Log($"Failed to acquire blob container when deploying linux consumption app");
                throw new DeploymentFailedException(ex);
            }

            string sitename = ServerConfiguration.GetApplicationName();
            string placeholderBlobName = $"{Constants.ScmRunFromPackageBlobPrefix}-{sitename}.squashfs";
            CloudBlockBlob blob = container.GetBlockBlobReference(placeholderBlobName);
            context.Logger.Log($"WEBSITE_RUN_FROM_PACKAGE placeholder blob {blob.Name} located");
            return blob;
        }

        private static CloudBlockBlob GetScmRunFromPackageBlob(DeploymentContext context, IDeploymentSettingsManager settings)
        {
            string scmSas = settings.GetValue(Constants.ScmRunFromPackage);
            if (string.IsNullOrEmpty(scmSas))
            {
                scmSas = System.Environment.GetEnvironmentVariable(Constants.ScmRunFromPackage);
            }

            if (!Uri.TryCreate(scmSas, UriKind.Absolute, out Uri scmUri)) {
                context.Logger.Log($"Malformed SCM_RUN_FROM_PACKAGE when uploading built content.");
                throw new DeploymentFailedException(new ArgumentException("Failed to upload because SAS is malformed."));
            }

            CloudBlockBlob blob = new CloudBlockBlob(scmUri);
            context.Logger.Log($"SCM_RUN_FROM_PACKAGE placeholder blob {blob.Name} located");
            return blob;
        }

        private static async Task UpdateWebsiteRunFromPackageAppSetting(DeploymentContext context, CloudBlockBlob blob)
        {
            context.Logger.Log($"Updating WEBSITE_RUN_FROM_PACKAGE app setting for linux consumption app...");

            SharedAccessBlobPolicy sasConstraints = new SharedAccessBlobPolicy();
            sasConstraints.SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5);
            sasConstraints.SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddYears(10);
            sasConstraints.Permissions = SharedAccessBlobPermissions.Read;
            string token = blob.GetSharedAccessSignature(sasConstraints);
            string sas = blob.Uri + token;

            try
            {
                await OperationManager.AttemptAsync(async () =>
                {
                    await PostDeploymentHelper.UpdateWebsiteRunFromPackage(sas, context);
                }, retries: 3, delayBeforeRetry: 2000);
            }
            catch (ArgumentException ae)
            {
                context.Logger.Log($"Set run from package has malformed blob sas {ae.Message}");
                throw new DeploymentFailedException(ae);
            }
            catch (HttpRequestException hre)
            {
                context.Logger.Log($"Set run from package endpoint responded with {hre.Message}");
                Exception innerException = hre.InnerException;
                while (innerException != null)
                {
                    context.Logger.Log($"Inner Exception = {innerException.Message}");
                    innerException = innerException.InnerException;
                }
                throw new DeploymentFailedException(hre);
            }
        }

        private static async Task RemoveLinuxConsumptionFunctionAppWorkers(DeploymentContext context)
        {
            string webSiteHostName = System.Environment.GetEnvironmentVariable(SettingsKeys.WebsiteHostname);
            string sitename = ServerConfiguration.GetApplicationName();

            context.Logger.Log($"Resetting all workers for {webSiteHostName}");

            try
            {
                await OperationManager.AttemptAsync(async () =>
                {
                    await PostDeploymentHelper.RemoveAllWorkersAsync(webSiteHostName, sitename);
                }, retries: 3, delayBeforeRetry: 2000);
            }
            catch (ArgumentException ae)
            {
                context.Logger.Log($"Reset all workers has malformed webSiteHostName or sitename {ae.Message}");
                throw new DeploymentFailedException(ae);
            }
            catch (HttpRequestException hre)
            {
                context.Logger.Log($"Reset all workers endpoint responded with {hre.Message}");
                throw new DeploymentFailedException(hre);
            }
        }

        private static async Task FunctionHostSyncTriggersAsync(DeploymentContext context)
        {
            string webSiteHostName = System.Environment.GetEnvironmentVariable(SettingsKeys.WebsiteHostname);

            context.Logger.Log($"Synchronizing function triggers for your function app {webSiteHostName}");

            try
            {
                await OperationManager.AttemptAsync(async () =>
                {
                    await PostDeploymentHelper.PerformFunctionHostSyncTriggersAsync(webSiteHostName);
                }, retries: 3, delayBeforeRetry: 2000);
            } catch (HttpRequestException hre)
            {
                context.Logger.Log($"Function triggers synchronization failed due to {hre.Message}");
            }
        }

        private static string PackageArtifactFromFolder(IEnvironment environment, IDeploymentSettingsManager settings, DeploymentContext context, string srcDirectory, string artifactDirectory, string artifactFilename)
        {
            context.Logger.Log("Writing the artifacts to a squashfs file");
            string file = Path.Combine(artifactDirectory, artifactFilename);
            ExternalCommandFactory commandFactory = new ExternalCommandFactory(environment, settings, context.RepositoryPath);
            Executable exe = commandFactory.BuildExternalCommandExecutable(srcDirectory, artifactDirectory, context.Logger);
            try
            {
                exe.ExecuteWithProgressWriter(context.Logger, context.Tracer, $"mksquashfs . {file} -noappend");
            }
            catch (Exception)
            {
                context.GlobalLogger.LogError();
                throw;
            }

            int numOfArtifacts = OryxBuildConstants.FunctionAppBuildSettings.ConsumptionBuildMaxFiles;
            DeploymentHelper.PurgeBuildArtifactsIfNecessary(artifactDirectory, BuildArtifactType.Squashfs, context.Tracer, numOfArtifacts);
            return file;
        }
    }
}
