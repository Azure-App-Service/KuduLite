using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kudu.Contracts.Tracing;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Deployment.Oryx;
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
    }
}
