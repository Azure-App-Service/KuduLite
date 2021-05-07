using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using Kudu.Core.Deployment;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kudu.Tests.Core.Deployment
{
    public class DeploymentManagerTests
    {
        [Fact]
        public void GetAppSettingsTest()
        {
            var appSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var functionsWorkerRuntimeKey = "APPSETTING_FUNCTIONS_WORKER_RUNTIME";
            var functionsWorkerRuntimeValue = "java";
            appSettings[functionsWorkerRuntimeKey] = functionsWorkerRuntimeValue;
            var functionsExtensionVersionKey = "appsetting_functions_extension_version";
            var functionsExtensionVersionValue = "~3";
            appSettings[functionsExtensionVersionKey] = functionsExtensionVersionValue;
        
            IHttpContextAccessor accessor = new HttpContextAccessor();
            accessor.HttpContext = new DefaultHttpContext();
            accessor.HttpContext.Items.Add("appSettings", appSettings);

            Assert.Equal(2, DeploymentManager.GetAppSettings(accessor, () => true).Count);
            Assert.Equal(functionsWorkerRuntimeValue, DeploymentManager.GetAppSettings(accessor, () => true)[functionsWorkerRuntimeKey]);
            Assert.Equal(functionsExtensionVersionValue, DeploymentManager.GetAppSettings(accessor, () => true)[functionsExtensionVersionKey]);
            Assert.Equal(0, DeploymentManager.GetAppSettings(accessor, () => false).Count);
            Assert.Equal(0, DeploymentManager.GetAppSettings(null, () => true).Count);
        }
    }
}
