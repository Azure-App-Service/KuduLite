using Microsoft.AspNetCore.Http;
using System;
using System.Linq;

namespace Kudu.Services.Util
{
    public static class RequestHelper
    {
        public static bool MediaTypeContains(this HttpRequest request, string lookupValue)
        {
            if (string.IsNullOrWhiteSpace(request.ContentType))
            {
                return false;
            }

            var tokens = request.ContentType.Split(';').Where(token =>
            {
                return token.Trim().Equals(lookupValue, StringComparison.OrdinalIgnoreCase);
            });

            return tokens.Any();
        }
    }
}
