﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Infrastructure;
using Kudu.Core.Settings;
using Kudu.Core.Tracing;
using Kudu.Services.Diagnostics;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Kudu.Services.Infrastructure;
using System.Threading.Tasks;

namespace Kudu.Services.Performance
{
    public class DiagnosticsController : Controller
    {
        // Matches Docker log filenames of logs that haven't been rolled (are most current for a given machine name)
        // Format is YYYY_MM_DD_<machinename>_docker[.<roll_number>].log
        // Examples:
        //   2017_08_23_RD00155DD0D38E_docker.log (not rolled)
        //   2017_08_23_RD00155DD0D38E_docker.1.log (rolled)
        private static readonly Regex NONROLLED_DOCKER_LOG_FILENAME_REGEX = new Regex(@"^\d{4}_\d{2}_\d{2}_.*_docker\.log$");

        private readonly DiagnosticsSettingsManager _settingsManager;
        private readonly string[] _paths;
        private readonly ITracer _tracer;
        private readonly IApplicationLogsReader _applicationLogsReader;
        private readonly IEnvironment _environment;

        public DiagnosticsController(IEnvironment environment, ITracer tracer, IApplicationLogsReader applicationLogsReader)
        {
            // Setup the diagnostics service to collect information from the following paths:
            // 1. The deployments folder
            // 2. The profile dump
            // 3. The npm log
            _paths = new[] {
                environment.DeploymentsPath,
                Path.Combine(environment.RootPath, Constants.LogFilesPath),
                Path.Combine(environment.WebRootPath, Constants.NpmDebugLogFile),
            };

            _environment = environment;
            _applicationLogsReader = applicationLogsReader;
            _tracer = tracer;
            _settingsManager = new DiagnosticsSettingsManager(Path.Combine(environment.DiagnosticsPath, Constants.SettingsJsonFile), _tracer);
        }

        /// <summary>
        /// Get all the diagnostic logs as a zip file
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [SuppressMessage("Microsoft.Usage", "CA2202", Justification = "The ZipArchive is instantiated in a way that the stream is not closed on dispose")]
        public IActionResult GetLog()
        {
            return new FileCallbackResult("application/zip", (outputStream, _) =>
            {
                using (var zip = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: false))
                {
                    AddFilesToZip(zip);
                }

                return Task.CompletedTask;
            })
            {
                FileDownloadName = String.Format("dump-{0:MM-dd-HH-mm-ss}.zip", DateTime.UtcNow)
            };
        }

        [HttpGet]
        public IActionResult GetRecentLogs(int top = 100)
        {
            using (_tracer.Step("DiagnosticsController.GetRecentLogs"))
            {
                var results = _applicationLogsReader.GetRecentLogs(top);
                return Ok(results);
            }
        }

        // Route only exists for this on Linux
        // Grabs "currently relevant" Docker logs from the LogFiles folder
        // and returns a JSON response with links to the files in the VFS API
        [HttpGet]
        public IActionResult GetDockerLogs(HttpRequestMessage request)
        {
            using (_tracer.Step("DiagnosticsController.GetDockerLogs"))
            {
                var currentDockerLogFilenames = GetCurrentDockerLogFilenames();

                var vfsBaseAddress = UriHelper.MakeRelative(
                    UriHelper.GetBaseUri(Request), "api/vfs");

                // Open files in order to refresh (not update) the timestamp and file size.
                // This is needed on Linux due to the way that metadata for files on the CIFS
                // mount gets cached and not always refreshed. Limit to 10 as a safety.
                // CORE TODO Should be using a filesystemhelper here and not File directly?

                foreach (var filename in currentDockerLogFilenames.Take(10))
                {
                    using (var file = System.IO.File.OpenRead(filename))
                    {
                        // This space intentionally left blank
                    }
                }

                var responseContent = currentDockerLogFilenames.Select(p => CurrentDockerLogFilenameToJson(p, vfsBaseAddress.ToString()));

                return Ok(responseContent);
            }
        }

        // Route only exists for this on Linux
        // Grabs "currently relevant" Docker logs from the LogFiles folder
        // and returns them in a zip archive
        [HttpGet]
        [SuppressMessage("Microsoft.Usage", "CA2202", Justification = "The ZipArchive is instantiated in a way that the stream is not closed on dispose")]
        public IActionResult GetDockerLogsZip()
        {
            using (_tracer.Step("DiagnosticsController.GetDockerLogsZip"))
            {
                var currentDockerLogFilenames = GetCurrentDockerLogFilenames();

                return new FileCallbackResult("application/zip", (outputStream, _) =>
                {
                    using (var zip = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: false))
                    {
                        foreach (var filename in currentDockerLogFilenames)
                        {
                            zip.AddFile(filename, _tracer);
                        }
                    }

                    return Task.CompletedTask;
                })
                {
                    FileDownloadName = String.Format("dockerlogs-{0:MM-dd-HH-mm-ss}.zip", DateTime.UtcNow)
                };
            }
        }

        private string[] GetCurrentDockerLogFilenames()
        {
            // Get all non-rolled Docker log filenames from the LogFiles directory
            var nonRolledDockerLogFilenames =
                FileSystemHelpers.ListFiles(_environment.LogFilesPath, SearchOption.TopDirectoryOnly, new[] { "*" })
                .Where(f => NONROLLED_DOCKER_LOG_FILENAME_REGEX.IsMatch(Path.GetFileName(f)))
                .ToArray();

            if (!nonRolledDockerLogFilenames.Any())
            {
                return new string[0];
            }

            // Find the latest date stamp and filter out those that don't have it
            // Timestamps are YYYY_MM_DD (sortable as integers with the underscores removed)
            var latestDatestamp = nonRolledDockerLogFilenames
                .Select(p => Path.GetFileName(p).Substring(0, 10))
                .OrderByDescending(s => int.Parse(s.Replace("_", String.Empty)))
                .First();

            return nonRolledDockerLogFilenames
                .Where(f => Path.GetFileName(f).StartsWith(latestDatestamp, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private JObject CurrentDockerLogFilenameToJson(string path, string vfsBaseAddress)
        {
            var info = new FileInfo(path);

            // Machine name is the middle portion of the filename, between the datestamp prefix
            // and the _docker.log suffix.
            var machineName = info.Name.Substring(11, info.Name.Length - 22);

            // Remove the root path from the front of the FullName, as it's implicit in the vfs url
            var vfsPath = info.FullName.Remove(0, _environment.RootPath.Length);

            var vfsUrl = (vfsBaseAddress + Uri.EscapeUriString(vfsPath)).EscapeHashCharacter();

            return new JObject(
                new JProperty("machineName", machineName),
                new JProperty("lastUpdated", info.LastWriteTimeUtc),
                new JProperty("size", info.Length),
                new JProperty("href", vfsUrl),
                new JProperty("path", info.FullName));
        }

        private void AddFilesToZip(ZipArchive zip)
        {
            foreach (var path in _paths)
            {
                if (Directory.Exists(path))
                {
                    var dir = new DirectoryInfo(path);
                    if (path.EndsWith(Constants.LogFilesPath, StringComparison.Ordinal))
                    {
                        foreach (var info in dir.GetFileSystemInfos())
                        {
                            var directoryInfo = info as DirectoryInfo;
                            if (directoryInfo != null)
                            {
                                // excluding FREB as it contains user sensitive data such as authorization header
                                if (!info.Name.StartsWith("W3SVC", StringComparison.OrdinalIgnoreCase))
                                {
                                    zip.AddDirectory(directoryInfo, _tracer, Path.Combine(dir.Name, info.Name));
                                }
                            }
                            else
                            {
                                zip.AddFile((FileInfo)info, _tracer, dir.Name);
                            }
                        }
                    }
                    else
                    {
                        zip.AddDirectory(dir, _tracer, Path.GetFileName(path));
                    }
                }
                // CORE TODO use filesystemhelper?
                else if (System.IO.File.Exists(path))
                {
                    zip.AddFile(path, _tracer, String.Empty);
                }
            }
        }

        public IActionResult Set(DiagnosticsSettings newSettings)
        {
            if (newSettings == null)
            {
                return StatusCode(StatusCodes.Status400BadRequest);
            }

            _settingsManager.UpdateSettings(newSettings);

            return NoContent();
        }

        public IActionResult Delete(string key)
        {
            if (String.IsNullOrEmpty(key))
            {
                return StatusCode(StatusCodes.Status400BadRequest);
            }

            _settingsManager.DeleteSetting(key);

            return NoContent();
        }

        public IActionResult GetAll()
        {
            return Ok(_settingsManager.GetSettings());
        }

        public IActionResult Get(string key)
        {
            if (String.IsNullOrEmpty(key))
            {
                return StatusCode(StatusCodes.Status400BadRequest);
            }

            object value = _settingsManager.GetSetting(key);

            if (value == null)
            {
                return NotFound(String.Format(Resources.SettingDoesNotExist, key));
            }

            return Ok(value);
        }
    }
}
