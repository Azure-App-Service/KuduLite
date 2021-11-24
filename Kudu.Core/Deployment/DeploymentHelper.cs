using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Kudu.Contracts.Tracing;
using Kudu.Core.Deployment.Oryx;
using Kudu.Core.Helpers;
using Kudu.Core.Infrastructure;
using Kudu.Core.SourceControl;
using Kudu.Core.Tracing;

namespace Kudu.Core.Deployment
{
    public static class DeploymentHelper
    {
        // build using msbuild in Azure
        // does not include njsproj(node), pyproj(python), they are built differently
        private static readonly string[] _projectFileExtensions = new[] { ".csproj", ".vbproj", ".fsproj", ".xproj" };

        public static readonly string[] ProjectFileLookup = _projectFileExtensions.Select(p => "*" + p).ToArray();

        public static IList<string> GetMsBuildProjects(string path, IFileFinder fileFinder, SearchOption searchOption = SearchOption.AllDirectories)
        {
            IEnumerable<string> filesList = fileFinder.ListFiles(path, searchOption, ProjectFileLookup);
            return filesList.ToList();
        }

        public static bool IsMsBuildProject(string path)
        {
            return _projectFileExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsDefaultWebRootContent(string webroot)
        {
            if (!FileSystemHelpers.DirectoryExists(webroot))
            {
                // degenerated
                return true;
            }

            var entries = FileSystemHelpers.GetFileSystemEntries(webroot);
            if (entries.Length == 0)
            {
                // degenerated
                return true;
            }

            if (entries.Length == 1 && FileSystemHelpers.FileExists(entries[0]))
            {
                string hoststarthtml = Path.Combine(webroot, Constants.HostingStartHtml);
                return String.Equals(entries[0], hoststarthtml, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public static void PurgeBuildArtifactsIfNecessary(string sitePackagesPath, BuildArtifactType fileExtension, ITracer tracer, int totalAllowedFiles)
        {
            string extension = fileExtension.ToString().ToLowerInvariant();
            IEnumerable<string> fileNames = FileSystemHelpers.GetFiles(sitePackagesPath, $"*.{extension}");
            if (fileNames.Count() > totalAllowedFiles)
            {
                // Order the files in descending order of the modified date and remove the last (N - allowed zip files).
                var fileNamesToDelete = fileNames.OrderByDescending(fileName => FileSystemHelpers.GetLastWriteTimeUtc(fileName)).Skip(totalAllowedFiles);
                foreach (var fileName in fileNamesToDelete)
                {
                    using (tracer.Step("Deleting outdated zip file {0}", fileName))
                    {
                        try
                        {
                            File.Delete(fileName);
                        }
                        catch (Exception ex)
                        {
                            tracer.TraceError(ex, "Unable to delete zip file {0}", fileName);
                        }
                    }
                }
            }
        }

        public static Task LocalZipFetch(string zipTempPath, IRepository repository, DeploymentInfoBase deploymentInfo, ILogger logger, ITracer tracer)
        {
            var zipDeploymentInfo = (ArtifactDeploymentInfo)deploymentInfo;

            // For this kind of deployment, RepositoryUrl is a local path.
            var sourceZipFile = zipDeploymentInfo.RepositoryUrl;
            var extractTargetDirectory = repository.RepositoryPath;

            var info = FileSystemHelpers.FileInfoFromFileName(sourceZipFile);
            var sizeInMb = (info.Length / (1024f * 1024f)).ToString("0.00", CultureInfo.InvariantCulture);

            var message = String.Format(
                CultureInfo.InvariantCulture,
                "Cleaning up temp folders from previous zip deployments and extracting pushed zip file {0} ({1} MB) to {2}",
                info.FullName,
                sizeInMb,
                extractTargetDirectory);

            logger.Log(message);

            using (tracer.Step(message))
            {
                // If extractTargetDirectory already exists, rename it so we can delete it concurrently with
                // the unzip (along with any other junk in the folder)
                var targetInfo = FileSystemHelpers.DirectoryInfoFromDirectoryName(extractTargetDirectory);
                if (targetInfo.Exists)
                {
                    var moveTarget = Path.Combine(targetInfo.Parent.FullName, Path.GetRandomFileName());
                    targetInfo.MoveTo(moveTarget);
                }

                DeleteFilesAndDirsExcept(sourceZipFile, extractTargetDirectory, zipTempPath, tracer);

                FileSystemHelpers.CreateDirectory(extractTargetDirectory);

                using (var file = info.OpenRead())

                using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    deploymentInfo.repositorySymlinks = zip.Extract(extractTargetDirectory);

                    if (!OSDetector.IsOnWindows())
                    {
                        CreateZipSymlinks(deploymentInfo.repositorySymlinks, extractTargetDirectory);
                        PermissionHelper.ChmodRecursive("777", extractTargetDirectory, tracer, TimeSpan.FromMinutes(1));
                    }
                }
            }

            CommitRepo(repository, zipDeploymentInfo);
            return Task.CompletedTask;
        }

        public static void DeleteFilesAndDirsExcept(string fileToKeep, string dirToKeep, string zipTempPath, ITracer tracer)
        {
            // Best effort. Using the "Safe" variants does retries and swallows exceptions but
            // we may catch something non-obvious.
            try
            {
                var files = FileSystemHelpers.GetFiles(zipTempPath, "*")
                    .Where(p => !PathUtilityFactory.Instance.PathsEquals(p, fileToKeep));

                foreach (var file in files)
                {
                    FileSystemHelpers.DeleteFileSafe(file);
                }

                var dirs = FileSystemHelpers.GetDirectories(zipTempPath)
                    .Where(p => !PathUtilityFactory.Instance.PathsEquals(p, dirToKeep));

                foreach (var dir in dirs)
                {
                    FileSystemHelpers.DeleteDirectorySafe(dir);
                }
            }
            catch (Exception ex)
            {
                tracer.TraceError(ex, "Exception encountered during zip folder cleanup");
                throw;
            }
        }

        private static void CreateZipSymlinks(IDictionary<string, string> symLinks, string extractTargetDirectory)
        {
            if (!OSDetector.IsOnWindows() && symLinks != null)
            {
                foreach (var symlinkPair in symLinks)
                {
                    string symLinkFilePath = Path.Combine(extractTargetDirectory, symlinkPair.Key);
                    FileSystemHelpers.EnsureDirectory(FileSystemHelpers.GetDirectoryName(Path.Combine(extractTargetDirectory, symlinkPair.Key)));
                    FileSystemHelpers.CreateRelativeSymlinks(symLinkFilePath, symlinkPair.Value, TimeSpan.FromSeconds(5));
                }
            }
        }

        public static void CommitRepo(IRepository repository, ArtifactDeploymentInfo zipDeploymentInfo)
        {
            // Needed in order for repository.GetChangeSet() to work.
            // Similar to what OneDriveHelper and DropBoxHelper do.
            // We need to make to call respository.Commit() since deployment flow expects at
            // least 1 commit in the IRepository. Even though there is no repo per se in this
            // scenario, deployment pipeline still generates a NullRepository
            repository.Commit(zipDeploymentInfo.Message, zipDeploymentInfo.Author, zipDeploymentInfo.AuthorEmail);
        }
    }
}
