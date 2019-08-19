using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Internal;
using System;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Kudu.Core.Infrastructure;
using Kudu.Core.Helpers;
using System.Threading;

namespace Kudu.Services.Zip
{
    // Extending VfsControllerBase is a slight abuse since this has nothing to do with vfs. But there is a lot
    // of good reusable logic in there. We could consider extracting a more basic base class from it.
    public class ZipController : VfsControllerBase
    {
        private ITracer _tracer;
        private IEnvironment _environment;
        public ZipController(ITracer tracer, IEnvironment environment)
            : base(tracer, environment, environment.RootPath)
        {
            _tracer = tracer;
            _environment = environment;

        }

        protected override Task<IActionResult> CreateDirectoryGetResponse(DirectoryInfoBase info, string localFilePath)
        {
            if (!Request.Query.TryGetValue("fileName", out var fileName))
            {
                fileName = Path.GetFileName(Path.GetDirectoryName(localFilePath)) + ".zip";
            }

            var result = new FileCallbackResult("application/zip", (outputStream, _) =>
            {
                // Note that a stream wrapper is no longer needed for ZipArchive, this was fixed in its implementation.
                using (var zip = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: false))
                {
                    foreach (FileSystemInfoBase fileSysInfo in info.GetFileSystemInfos())
                    {
                        var directoryInfo = fileSysInfo as DirectoryInfoBase;
                        if (directoryInfo != null)
                        {
                            zip.AddDirectory(directoryInfo, Tracer, fileSysInfo.Name);
                        }
                        else
                        {
                            // Add it at the root of the zip
                            zip.AddFile(fileSysInfo.FullName, Tracer, String.Empty);
                        }
                    }
                }

                return Task.CompletedTask;
            })
            {
                FileDownloadName = fileName
            };

            return Task.FromResult((IActionResult)result);
        }

        protected override Task<IActionResult> CreateItemGetResponse(FileSystemInfoBase info, string localFilePath)
        {
            // We don't support getting a file from the zip controller
            // Conceivably, it could be a zip file containing just the one file, but that's rarely interesting
            return Task.FromResult((IActionResult)NotFound());
        }

        protected override Task<IActionResult> CreateDirectoryPutResponse(DirectoryInfoBase info, string localFilePath)
        {
            var zipArchive = new ZipArchive(Request.Body, ZipArchiveMode.Read);
            zipArchive.Extract(localFilePath);
            PermissionHelper.ChmodRecursive("777", localFilePath, _tracer, TimeSpan.FromSeconds(30));
            return Task.FromResult((IActionResult)Ok());
        }

        protected override Task<IActionResult> CreateItemPutResponse(FileSystemInfoBase info, string localFilePath, bool itemExists)
        {
            // We don't support putting an individual file using the zip controller
            return Task.FromResult((IActionResult)NotFound());
        }
    }
}
