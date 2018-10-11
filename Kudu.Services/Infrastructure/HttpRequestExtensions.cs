using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

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

    }
}