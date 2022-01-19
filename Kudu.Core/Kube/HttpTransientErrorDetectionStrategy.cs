using System;
using System.Net;
using Microsoft.Rest;
using Microsoft.Rest.TransientFaultHandling;

namespace Kudu.Core.Kube
{
    public class HttpTransientErrorDetectionStrategy : ITransientErrorDetectionStrategy
    {
        public bool IsTransient(Exception ex)
        {
            var httpOperationEx = ex as HttpOperationException;

            if (httpOperationEx != null)
            {
                var statusCode = httpOperationEx.Response?.StatusCode;
                return statusCode == HttpStatusCode.ServiceUnavailable
                    || statusCode == HttpStatusCode.BadGateway
                    || statusCode == HttpStatusCode.GatewayTimeout;
            }

            return false;
        }
    }
}
