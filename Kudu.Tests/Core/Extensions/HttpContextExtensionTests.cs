using System;
using System.Collections.Generic;
using Kudu.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Kudu.Tests.Core.Extensions
{
    public class HttpContextExtensionTests
    {
        [Fact]
        public void GetAppSettingsTest()
        {

            var appSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "key1", "value1" },
                { "KEY2", "Value2" }
            };
            
            Func<IDictionary<string, string>> funcSetAppSettings = () => appSettings;

            IHttpContextAccessor accessor = new HttpContextAccessor();
            accessor.HttpContext = new DefaultHttpContext();
            accessor.HttpContext.SetAppSettings(() => appSettings);

            foreach (var kv in accessor.HttpContext.GetAppSettings())
            {
                Assert.Equal(kv.Value, appSettings[kv.Key]);
            }
        }
    }
}
