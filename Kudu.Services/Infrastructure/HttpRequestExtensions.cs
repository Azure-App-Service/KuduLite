using Kudu.Core.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.Http.Extensions;

namespace Kudu.Services.Infrastructure
{
    public static class HttpRequestExtensions
    {
        public static string GetRequestId(this HttpRequest httpRequest)
        {
            // prefer x-arr-log-id over x-ms-request-id since azure always populates the former.
            return httpRequest.Headers.TryGetValue(Constants.ArrLogIdHeader, out StringValues arrLogId)
                ? arrLogId.ToString()
                : httpRequest.Headers[Constants.RequestIdHeader].ToString();
        }

        public static string GetUserAgent(this HttpRequest httpRequest)
        {
            return httpRequest.Headers["User-Agent"].ToString();
        }

        /// <summary>
        /// When the container running in SeaBreeze, the container will receive request with "http://~1sitename" in the url
        /// We need to sanitize it otherwise it will break "new Uri(request.GetDisplayUrl())" call
        /// </summary>
        /// <param name="httpRequest">The http request from frontend</param>
        /// <returns>A url without ~1</returns>
        public static string GetSanitizedDisplayUrl(this HttpRequest httpRequest)
        {
            return ScmSiteUrlHelper.SanitizeUrl(httpRequest.GetDisplayUrl());
        }
    }
}