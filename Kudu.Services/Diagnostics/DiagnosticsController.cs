using System;
using System.Collections.Generic;
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
    class DockerLogSourceMap
    {
        public string LogSource { get; set; } // RDxxxxxxxxxxxx + "_" + default, easyauth, "" (i.e. Docker), etc.
        public DateTime Timestamp { get; set; }  // YYYYmmDD
        public List<string> Paths { get; set; }
    }

    public class DiagnosticsController : Controller
    {
        // Matches Docker log filenames of logs that haven't been rolled (are most current for a given machine name)
        // Format is YYYY_MM_DD_<machinename>_docker[.<roll_number>].log
        // Examples:
        //   2017_08_23_RD00155DD0D38E_docker.log (not rolled)
        //   2017_08_23_RD00155DD0D38E_docker.1.log (rolled)
        private static readonly Regex NONROLLED_DOCKER_LOG_FILENAME_REGEX = new Regex(@"^(\d{4}_\d{2}_\d{2})_(.*)_docker\.log$");

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
                var currentDockerLogFilenames = GetCurrentDockerLogFilenames(SearchOption.TopDirectoryOnly);

                var vfsBaseAddress = UriHelper.MakeRelative(
                    UriHelper.GetBaseUri(Request), "api/vfs");

                // Open files in order to refresh (not update) the timestamp and file size.
                // This is needed on Linux due to the way that metadata for files on the CIFS
                // mount gets cached and not always refreshed. Limit to 10 as a safety.
                // CORE TODO Should be using a filesystemhelper here and not File directly?

                foreach (var filename in currentDockerLogFilenames.Take(10))
                {
                    // LWAS can have file open handle during a rollover,
                    // this is to prevent this api from throwing an exception in
                    // such a situation
                    try
                    {
                        using (var file = System.IO.File.OpenRead(filename))
                        {
                            // This space intentionally left blank
                        }
                    }
                    catch(Exception ex)
                    {
                        _tracer.TraceError(ex);
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
                // Also search for "today's" files in sub folders. Windows containers archives log files
                // when they reach a certain size.
                var currentDockerLogFilenames = GetCurrentDockerLogFilenames(SearchOption.AllDirectories);

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

        private string[] GetCurrentDockerLogFilenames(SearchOption searchOption)
        {
            var allDockerLogFilenames = FileSystemHelpers.ListFiles(_environment.LogFilesPath, searchOption, new[] { "*" }).ToArray();
            var logSources = new Dictionary<string, DockerLogSourceMap>();

            // Get all non-rolled Docker log filenames from the LogFiles directory
            foreach (var dockerLogPath in allDockerLogFilenames)
            {
                var match = NONROLLED_DOCKER_LOG_FILENAME_REGEX.Match(Path.GetFileName(dockerLogPath));
                if (match.Success)
                {
                    // Get the timestamp and log source (machine name "_" default, easyauth, empty(Docker), etc) from the file name
                    // and find the latest one for each source
                    // Note that timestamps are YYYY_MM_DD (sortable as integers with the underscores removed)
                    DateTime date;
                    if (!DateTime.TryParse(match.Groups[1].Value.Replace("_", "/"), out date))
                    {
                        continue;
                    }
                    var source = match.Groups[2].Value;

                    if (!logSources.ContainsKey(source) || logSources[source].Timestamp.CompareTo(date) < 0)
                    {
                        logSources[source] = new DockerLogSourceMap {
                            LogSource = source,
                            Timestamp = date,
                            Paths = new List<string> { dockerLogPath }
                        };
                    }
                    else
                    {
                        logSources[source].Paths.Add(dockerLogPath);
                    }
                }
            }

            if (logSources.Keys.Count == 0)
            {
                return new string[0];
            }

            var timeStampThreshold = logSources.Values.Max(v => v.Timestamp).AddDays(-7);
            return logSources.Values.Where(v => v.Timestamp >= timeStampThreshold).SelectMany(m => m.Paths).ToArray();
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
                else if (FileSystemHelpers.FileExists(path))
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
