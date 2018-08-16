using System;
using System.Diagnostics.Contracts;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kudu.Common;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Tracing;
using Kudu.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Kudu.Services.Editor
{
    /// <summary>
    /// A Virtual File System controller which exposes GET, PUT, and DELETE for the entire Kudu file system.
    /// </summary>
    public class VfsController : VfsControllerBase
    {
        public VfsController(ITracer tracer, IEnvironment environment)
            : base(tracer, environment, environment.RootPath)
        {
        }

        protected override Task<IActionResult> CreateDirectoryPutResponse(DirectoryInfoBase info, string localFilePath)
        {
            if (info != null && info.Exists)
            {
                // Return a conflict result
                return base.CreateDirectoryPutResponse(info, localFilePath);
            }

            try
            {
                info.Create();
            }
            catch (IOException ex)
            {
                Tracer.TraceError(ex);
                return Task.FromResult((IActionResult)StatusCode(StatusCodes.Status409Conflict, Resources.VfsControllerBase_CannotDeleteDirectory));
            }

            // Return 201 Created response
            return Task.FromResult((IActionResult)StatusCode(StatusCodes.Status201Created));
        }

        protected override Task<IActionResult> CreateItemGetResponse(FileSystemInfoBase info, string localFilePath)
        {
            // CORE TODO File() apparently has built in support for range requests and etags. From a cursory glance
            // that renders bascially all of commented implementation belowthis obsolete. Will need checking to ensure proper behavior.
            var fileStream = GetFileReadStream(localFilePath);
            return Task.FromResult(
                (IActionResult)File(fileStream, MediaTypeMap.GetMediaType(info.Extension).ToString(), info.LastWriteTime, CreateEntityTag(info)));

            /*
            // Get current etag
            EntityTagHeaderValue currentEtag = CreateEntityTag(info);
            DateTime lastModified = info.LastWriteTimeUtc;

            // Check whether we have a range request (taking If-Range condition into account)
            bool isRangeRequest = IsRangeRequest(currentEtag);

            // Check whether we have a conditional If-None-Match request
            // Unless it is a range request (see RFC2616 sec 14.35.2 Range Retrieval Requests)
            if (!isRangeRequest && IsIfNoneMatchRequest(currentEtag))
            {
                Response.SetEntityTagHeader(currentEtag, lastModified);
                return Task.FromResult((IActionResult)StatusCode(StatusCodes.Status304NotModified)));
            }

            // Generate file response
            Stream fileStream = null;
            try
            {
                fileStream = GetFileReadStream(localFilePath);
                MediaTypeHeaderValue mediaType = MediaTypeMap.GetMediaType(info.Extension);
                HttpResponseMessage successFileResponse = Request.CreateResponse(isRangeRequest ? HttpStatusCode.PartialContent : HttpStatusCode.OK);

                if (isRangeRequest)
                {
                    successFileResponse.Content = new ByteRangeStreamContent(fileStream, Request.Headers.Range, mediaType, BufferSize);
                }
                else
                {
                    successFileResponse.Content = new StreamContent(fileStream, BufferSize);
                    successFileResponse.Content.Headers.ContentType = mediaType;
                }

                // Set etag for the file
                successFileResponse.SetEntityTagHeader(currentEtag, lastModified);
                return Task.FromResult(successFileResponse);
            }
            catch (InvalidByteRangeException invalidByteRangeException)
            {
                // The range request had no overlap with the current extend of the resource so generate a 416 (Requested Range Not Satisfiable)
                // including a Content-Range header with the current size.
                Tracer.TraceError(invalidByteRangeException);
                HttpResponseMessage invalidByteRangeResponse = Request.CreateErrorResponse(invalidByteRangeException);
                if (fileStream != null)
                {
                    fileStream.Close();
                }
                return Task.FromResult(invalidByteRangeResponse);
            }
            catch (Exception ex)
            {
                // Could not read the file
                Tracer.TraceError(ex);
                HttpResponseMessage errorResponse = Request.CreateErrorResponse(HttpStatusCode.NotFound, ex);
                if (fileStream != null)
                {
                    fileStream.Close();
                }
                return Task.FromResult(errorResponse);
            }
            */
        }

        protected override async Task<IActionResult> CreateItemPutResponse(FileSystemInfoBase info, string localFilePath, bool itemExists)
        {
            // Check that we have a matching conditional If-Match request for existing resources
            if (itemExists)
            {
                var requestHeaders = Request.GetTypedHeaders();
                var responseHeaders = Response.GetTypedHeaders();

                // Get current etag
                EntityTagHeaderValue currentEtag = CreateEntityTag(info);

                // Existing resources require an etag to be updated.
                // CORE TODO Moved to Microsoft.Net.Http.Headers, double check semantics (null, empty string etc.)
                if (requestHeaders.IfMatch == null)
                {
                    return StatusCode(StatusCodes.Status412PreconditionFailed, Resources.VfsController_MissingIfMatch);
                }

                bool isMatch = false;
                foreach (EntityTagHeaderValue etag in requestHeaders.IfMatch)
                {
                    if (currentEtag.Compare(etag, false) || etag == EntityTagHeaderValue.Any)
                    {
                        isMatch = true;
                        break;
                    }
                }

                if (!isMatch)
                {
                    responseHeaders.ETag = currentEtag;
                    return StatusCode(StatusCodes.Status412PreconditionFailed, Resources.VfsController_EtagMismatch);
                }
            }

            // Save file
            try
            {
                using (Stream fileStream = GetFileWriteStream(localFilePath, fileExists: itemExists))
                {
                    try
                    {
                        await Request.Body.CopyToAsync(fileStream);
                    }
                    catch (Exception ex)
                    {
                        Tracer.TraceError(ex);
                        return StatusCode(
                            StatusCodes.Status409Conflict,
                            RS.Format(Resources.VfsController_WriteConflict, localFilePath, ex.Message));
                    }
                }

                // Return either 204 No Content or 201 Created response

                // Set updated etag for the file
                info.Refresh();
                Response.SetEntityTagHeader(CreateEntityTag(info), info.LastWriteTimeUtc);
                return itemExists ? NoContent() : StatusCode(StatusCodes.Status201Created);

            }
            catch (Exception ex)
            {
                Tracer.TraceError(ex);
                return StatusCode(StatusCodes.Status409Conflict, RS.Format(Resources.VfsController_WriteConflict, localFilePath, ex.Message));
            }
        }

        protected override Task<IActionResult> CreateFileDeleteResponse(FileInfoBase info)
        {
            // Existing resources require an etag to be updated.
            var requestHeaders = Request.GetTypedHeaders();

            // CORE TODO double check semantics of what you get from GetTypedHeaders() (empty strings vs null, etc.)
            if (requestHeaders.IfMatch == null)
            {
                return Task.FromResult((IActionResult)StatusCode(StatusCodes.Status412PreconditionFailed, Resources.VfsController_MissingIfMatch));
            }

            // Get current etag
            EntityTagHeaderValue currentEtag = CreateEntityTag(info);
            bool isMatch = requestHeaders.IfMatch.Any(etag => etag == EntityTagHeaderValue.Any || currentEtag.Equals(etag));

            if (!isMatch)
            {
                Response.GetTypedHeaders().ETag = currentEtag;
                return Task.FromResult((IActionResult)StatusCode(StatusCodes.Status409Conflict, Resources.VfsController_EtagMismatch));
            }

            return base.CreateFileDeleteResponse(info);
        }

        /// <summary>
        /// Create unique etag based on the last modified UTC time
        /// </summary>
        private static EntityTagHeaderValue CreateEntityTag(FileSystemInfoBase sysInfo)
        {
            Contract.Assert(sysInfo != null);
            byte[] etag = BitConverter.GetBytes(sysInfo.LastWriteTimeUtc.Ticks);

            var result = new StringBuilder(2 + etag.Length * 2);
            result.Append("\"");
            foreach (byte b in etag)
            {
                result.AppendFormat("{0:x2}", b);
            }
            result.Append("\"");
            return new EntityTagHeaderValue(result.ToString());
        }
    }
}
