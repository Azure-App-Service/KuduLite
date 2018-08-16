using Microsoft.AspNetCore.Http;
using System;
using System.Net.Http;
using Microsoft.Net.Http.Headers;

namespace Kudu.Services.Infrastructure
{
    internal static class HttpResponseMessageExtensions
    {
        public static void SetEntityTagHeader(this HttpResponse response, EntityTagHeaderValue etag, DateTime lastModified)
        {
            // CORE TODO Not sure if this is needed now, so commenting out.
            //if (httpResponseMessage.Content == null)
            //{
            //    httpResponseMessage.Content = new NullContent();
            //}

            // CORE TODO Now uses Microsoft.Net.Http.Headers, double check semantics to make sure
            // behavior hasn't changed
            var headers = response.GetTypedHeaders();
            headers.ETag = etag;
            headers.LastModified = lastModified;
        }

        class NullContent : StringContent
        {
            public NullContent()
                : base(String.Empty)
            {
                Headers.ContentType = null;
                Headers.ContentLength = null;
            }
        }
    }
}