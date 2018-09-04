#region License

// Copyright 2010 Jeremy Skinner (http://www.jeremyskinner.co.uk)
//  
// Licensed under the Apache License, Version 2.0 (the "License"); 
// you may not use this file except in compliance with the License. 
// You may obtain a copy of the License at 
// 
// http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software 
// distributed under the License is distributed on an "AS IS" BASIS, 
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and 
// limitations under the License.
// 
// The latest version of this file can be found at http://github.com/JeremySkinner/git-dot-aspx

// This file was modified from the one found in git-dot-aspx

#endregion

using Kudu.Contracts.Tracing;
using Kudu.Core.SourceControl.Git;
using Kudu.Core.Tracing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using System.Threading.Tasks;

namespace Kudu.Services.GitServer
{
    public class UploadPackHandlerMiddleware
    {
        public UploadPackHandlerMiddleware(RequestDelegate next)
        {
            // next is never used, this middleware is always terminal
        }

        public Task Invoke(
            HttpContext context,
            ITracer tracer,
            IGitServer gitServer)
        {
            using (tracer.Step("RpcService.UploadPackHandler"))
            {
                UpdateNoCacheForResponse(context.Response);

                context.Response.ContentType = "application/x-git-{0}-result".With("upload-pack");

                gitServer.Upload(context.Request.Body, context.Response.Body);
            }

            return Task.CompletedTask;
        }

        // CORE TODO pulled from GitServerHttpHandler and duplicated in both handlers
        public static void UpdateNoCacheForResponse(HttpResponse response)
        {
            // CORE TODO no longer exists
            //response.Buffer = false;
            //response.BufferOutput = false;

            response.Headers["Expires"] = "Fri, 01 Jan 1980 00:00:00 GMT";
            response.Headers["Pragma"] = "no-cache";
            response.Headers["Cache-Control"] = "no-cache, max-age=0, must-revalidate";
        }
    }

    public static class UploadPackHandlerExtensions
    {
        public static IApplicationBuilder RunUploadPackHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<UploadPackHandlerMiddleware>();
        }
    }
}
