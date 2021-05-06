using System;
using System.Collections.Generic;
using System.Text;
using Kudu.Core.K8SE;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Kudu.Tests.Core.K8SE
{

    public class K8SEDeploymentHelperTest
    {
        [Fact]
        public void UpdateHttpContextWithAppSettings()
        {
            HttpContext context = new DefaultHttpContext();
            // AppSettings are case insensitive
            // Current header casing inject header as lowercase.
            context.Request.Headers.Add("appsetting_azurewebjobsstorage", new StringValues("AccountKey=verySecure"));
            context.Request.Headers.Add("APPSETTING_FUNCTIONS_WORKER_RUNTIME", new StringValues("java"));
            context.Request.Headers.Add("FUNCTIONS_EXTENSION_VERSION", new StringValues("~3"));
            context.Request.Headers.Add("SOME_FUNCTIONS_WORKER_RUNTIME", new StringValues("Should not match"));
            K8SEDeploymentHelper.UpdateContextWithAppSettings(context);
            IDictionary<string, string> appSettings = (IDictionary<string, string>)context.Items["appSettings"];
            Assert.Equal("AccountKey=verySecure", appSettings["AzureWebJobsStorage"]);
            Assert.Equal("java", appSettings["FUNCTIONS_WORKER_RUNTIME"]);
            Assert.Equal(2, appSettings.Count);
        }
    }
}
