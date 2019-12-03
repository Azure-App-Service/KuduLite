using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Kudu.Contracts.Editor;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Kudu.Core.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace Kudu.Services.Infrastructure
{
    /// <summary>
    /// Provides common functionality for Virtual File System controllers.
    /// </summary>
    public abstract class VfsControllerBase : Controller
    {
        private const string DirectoryEnumerationSearchPattern = "*";
        public const char UriSegmentSeparator = '/';

        private static readonly char[] _uriSegmentSeparator = new char[] { UriSegmentSeparator };
        private static readonly MediaTypeHeaderValue _directoryMediaType = MediaTypeHeaderValue.Parse("inode/directory");

        protected const int BufferSize = 32 * 1024;

        protected VfsControllerBase(ITracer tracer, IEnvironment environment, string rootPath)
        {
            if (rootPath == null)
            {
                throw new ArgumentNullException("rootPath");
            }
            Tracer = tracer;
            Environment = environment;

            RootPath = Path.GetFullPath(rootPath.TrimEnd(Path.DirectorySeparatorChar));
            MediaTypeMap = MediaTypeMap.Default;
        }

        [AcceptVerbs("GET", "HEAD")]
        public virtual Task<IActionResult> GetItem()
        {
            string localFilePath = GetLocalFilePath();
            if (VfsSpecialFolders.TryHandleRequest(Request, localFilePath, out IActionResult response))
            {
                return Task.FromResult(response);
            }

            DirectoryInfoBase info = FileSystemHelpers.DirectoryInfoFromDirectoryName(localFilePath);

            if (info.Attributes < 0)
            {
                return Task.FromResult((IActionResult)NotFound(String.Format("'{0}' not found.", info.FullName)));
            }
            else if ((info.Attributes & FileAttributes.Directory) != 0)
            {
                // If request URI does NOT end in a "/" then redirect to one that does
                if (localFilePath[localFilePath.Length - 1] != Path.DirectorySeparatorChar)
                {
                    UriBuilder location = new UriBuilder(UriHelper.GetRequestUri(Request));
                    location.Path += "/";
                    return Task.FromResult((IActionResult)RedirectPreserveMethod(location.Uri.ToString()));
                }
                else
                {
                    return CreateDirectoryGetResponse(info, localFilePath);
                }
            }
            else
            {
                // If request URI ends in a "/" then redirect to one that does not
                if (localFilePath[localFilePath.Length - 1] == Path.DirectorySeparatorChar)
                {
                    UriBuilder location = new UriBuilder(UriHelper.GetRequestUri(Request));
                    location.Path = location.Path.TrimEnd(_uriSegmentSeparator);
                    return Task.FromResult((IActionResult)RedirectPreserveMethod(location.Uri.ToString()));
                }

                // We are ready to get the file
                return CreateItemGetResponse(info, localFilePath);
            }
        }

        [HttpPut]
        public virtual Task<IActionResult> PutItem()
        {
            string localFilePath = GetLocalFilePath();

            if (VfsSpecialFolders.TryHandleRequest(Request, localFilePath, out IActionResult response))
            {
                return Task.FromResult(response);
            }

            DirectoryInfoBase info = FileSystemHelpers.DirectoryInfoFromDirectoryName(localFilePath);
            bool itemExists = info.Attributes >= 0;

            if (itemExists && (info.Attributes & FileAttributes.Directory) != 0)
            {
                return CreateDirectoryPutResponse(info, localFilePath);
            }
            else
            {
                // If request URI ends in a "/" then attempt to create the directory.
                if (localFilePath[localFilePath.Length - 1] == Path.DirectorySeparatorChar)
                {
                    return CreateDirectoryPutResponse(info, localFilePath);
                }

                // We are ready to update the file
                return CreateItemPutResponse(info, localFilePath, itemExists);
            }
        }

        [HttpDelete]
        public virtual Task<IActionResult> DeleteItem(bool recursive = false)
        {
            string localFilePath = GetLocalFilePath();

            if (VfsSpecialFolders.TryHandleRequest(Request, localFilePath, out IActionResult response))
            {
                return Task.FromResult(response);
            }

            DirectoryInfoBase dirInfo = FileSystemHelpers.DirectoryInfoFromDirectoryName(localFilePath);

            if (dirInfo.Attributes < 0)
            {
                return Task.FromResult((IActionResult)NotFound(String.Format("'{0}' not found.", dirInfo.FullName)));
            }
            else if ((dirInfo.Attributes & FileAttributes.Directory) != 0)
            {
                try
                {
                    dirInfo.Delete(recursive);
                }
                catch (Exception ex)
                {
                    Tracer.TraceError(ex);
                    return Task.FromResult((IActionResult)StatusCode(StatusCodes.Status409Conflict, Resources.VfsControllerBase_CannotDeleteDirectory));
                }

                // Delete directory succeeded.
                return Task.FromResult((IActionResult)Ok());
            }
            else
            {
                // If request URI ends in a "/" then redirect to one that does not
                if (localFilePath[localFilePath.Length - 1] == Path.DirectorySeparatorChar)
                {
                    UriBuilder location = new UriBuilder(UriHelper.GetRequestUri(Request));
                    location.Path = location.Path.TrimEnd(_uriSegmentSeparator);
                    return Task.FromResult((IActionResult)RedirectPreserveMethod(location.Uri.ToString()));
                }

                // We are ready to delete the file
                var fileInfo = FileSystemHelpers.FileInfoFromFileName(localFilePath);
                return CreateFileDeleteResponse(fileInfo);
            }
        }

        protected ITracer Tracer { get; private set; }

        protected IEnvironment Environment { get; private set; }

        protected string RootPath { get; private set; }

        protected MediaTypeMap MediaTypeMap { get; private set; }

        protected virtual Task<IActionResult> CreateDirectoryGetResponse(DirectoryInfoBase info, string localFilePath)
        {
            Contract.Assert(info != null);
            try
            {
                // Enumerate directory
                IEnumerable<VfsStatEntry> directory = GetDirectoryResponse(info.GetFileSystemInfos());
                return Task.FromResult((IActionResult)Ok(directory));
            }
            catch (Exception e)
            {
                Tracer.TraceError(e);
                return Task.FromResult((IActionResult)StatusCode(StatusCodes.Status500InternalServerError, e.Message));
            }
        }

        protected abstract Task<IActionResult> CreateItemGetResponse(FileSystemInfoBase info, string localFilePath);

        protected virtual Task<IActionResult> CreateDirectoryPutResponse(DirectoryInfoBase info, string localFilePath)
        {
            return Task.FromResult((IActionResult)StatusCode(StatusCodes.Status409Conflict, Resources.VfsController_CannotUpdateDirectory));
        }

        protected abstract Task<IActionResult> CreateItemPutResponse(FileSystemInfoBase info, string localFilePath, bool itemExists);

        protected virtual Task<IActionResult> CreateFileDeleteResponse(FileInfoBase info)
        {
            // Generate file response
            try
            {
                using (Stream fileStream = GetFileDeleteStream(info))
                {
                    info.Delete();
                }
                return Task.FromResult((IActionResult)Ok());
            }
            catch (Exception e)
            {
                // Could not delete the file
                Tracer.TraceError(e);
                return Task.FromResult((IActionResult)NotFound(e));
            }
        }

        /// <summary>
        /// Indicates whether this is a conditional range request containing an
        /// If-Range header with a matching etag and a Range header indicating the 
        /// desired ranges
        /// </summary>
        protected bool IsRangeRequest(EntityTagHeaderValue currentEtag)
        {
            // CORE TODO Now using Microsoft.Net.Http.Headers, make sure it still has the same semantics
            // (null vs empty string, etc.)
            var headers = Request.GetTypedHeaders();
            if (headers.Range == null)
            {
                return false;
            }
            if (headers.IfRange != null)
            {
                headers.IfRange.EntityTag.Compare(currentEtag, false);
            }
            return true;
        }

        /// <summary>
        /// Indicates whether this is a If-None-Match request with a matching etag.
        /// </summary>
        protected bool IsIfNoneMatchRequest(EntityTagHeaderValue currentEtag)
        {
            // CORE TODO Now using Microsoft.Net.Http.Headers, make sure it still has the same semantics
            // (null vs empty string, etc.)
            var headers = Request.GetTypedHeaders();
            return currentEtag != null && headers.IfNoneMatch != null &&
                headers.IfNoneMatch.Any(entityTag => currentEtag.Compare(entityTag, false));
        }

        /// <summary>
        /// Provides a common way for opening a file stream for shared reading from a file.
        /// </summary>
        protected static Stream GetFileReadStream(string localFilePath)
        {
            Contract.Assert(localFilePath != null);

            // Open file exclusively for read-sharing
            return new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, useAsync: true);
        }

        /// <summary>
        /// Provides a common way for opening a file stream for writing exclusively to a file. 
        /// </summary>
        protected static Stream GetFileWriteStream(string localFilePath, bool fileExists)
        {
            Contract.Assert(localFilePath != null);

            // Create path if item doesn't already exist
            if (!fileExists)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localFilePath));
            }

            // Open file exclusively for write without any sharing
            return new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        }

        /// <summary>
        /// Provides a common way for opening a file stream for exclusively deleting the file. 
        /// </summary>
        private static Stream GetFileDeleteStream(FileInfoBase file)
        {
            Contract.Assert(file != null);

            // Open file exclusively for delete sharing only
            return file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        // internal for testing purpose
        internal string GetLocalFilePath()
        {
            // Restore the original extension if we had added a dummy
            // See comment in TraceModule.OnBeginRequest
            string result = GetOriginalLocalFilePath();
            if (result.EndsWith(Constants.DummyRazorExtension, StringComparison.Ordinal))
            {
                result = result.Substring(0, result.Length - Constants.DummyRazorExtension.Length);
            }

            return result;
        }

        private string GetOriginalLocalFilePath()
        {
            // CORE TODO No longer Request.GetRouteData(), just RouteData property on controller.
            // Make sure everything still works.

            string result;
            if (VfsSpecialFolders.TryParse(RouteData, out result))
            {
                return result;
            }

            result = RootPath;
            if (RouteData != null)
            {
                string path = RouteData.Values["path"] as string;
                if (!String.IsNullOrEmpty(path))
                {
                    result = FileSystemHelpers.GetFullPath(Path.Combine(result, path));
                }
                else
                {
                    string reqUri = UriHelper.GetRequestUri(Request).AbsoluteUri.Split('?').First();
                    if (reqUri[reqUri.Length - 1] == UriSegmentSeparator)
                    {
                        result = Path.GetFullPath(result + Path.DirectorySeparatorChar);
                    }
                }
            }
            return result;
        }

        private IEnumerable<VfsStatEntry> GetDirectoryResponse(FileSystemInfoBase[] infos)
        {
            var requestUri = UriHelper.GetRequestUri(Request);
            string baseAddress = requestUri.AbsoluteUri.Split('?').First();
            string query = requestUri.Query;
            foreach (FileSystemInfoBase fileSysInfo in infos)
            {
                bool isDirectory = (fileSysInfo.Attributes & FileAttributes.Directory) != 0;
                string mime = isDirectory ? _directoryMediaType.ToString() : MediaTypeMap.GetMediaType(fileSysInfo.Extension).ToString();
                string unescapedHref = isDirectory ? fileSysInfo.Name + UriSegmentSeparator : fileSysInfo.Name;
                long size = isDirectory ? 0 : ((FileInfoBase)fileSysInfo).Length;

                yield return new VfsStatEntry
                {
                    Name = fileSysInfo.Name,
                    MTime = fileSysInfo.LastWriteTimeUtc,
                    CRTime = fileSysInfo.CreationTimeUtc,
                    Mime = mime,
                    Size = size,
                    Href = (baseAddress + Uri.EscapeUriString(unescapedHref) + query).EscapeHashCharacter(),
                    Path = fileSysInfo.FullName
                };
            }

            // add special folders when requesting Root url
            if (RouteData != null && String.IsNullOrEmpty(RouteData.Values["path"] as string))
            {
                foreach (var entry in VfsSpecialFolders.GetEntries(baseAddress, query))
                {
                    yield return entry;
                }
            }
        }
    }
}
