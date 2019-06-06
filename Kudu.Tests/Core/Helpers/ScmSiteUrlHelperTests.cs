using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Kudu.Core.Helpers;

namespace Kudu.Tests.Core.Helpers
{
    public class ScmSiteUrlHelperTests
    {
        [Theory]
        [InlineData("", "")]
        [InlineData("https://azurewebsites.net", "https://azurewebsites.net")]
        [InlineData("https://functions.azurewebsites.net", "https://functions.azurewebsites.net")]
        [InlineData("https://~1functions.azurewebsites.net", "https://functions.azurewebsites.net")]
        [InlineData("https://~12functions.azurewebsites.net", "https://functions.azurewebsites.net")]
        [InlineData("https://~123functions.azurewebsites.net/api", "https://functions.azurewebsites.net/api")]
        [InlineData("ssh://~1234functions.azurewebsites.net/webssh", "ssh://functions.azurewebsites.net/webssh")]
        [InlineData("https://123functions.azurewebsites.net/api", "https://123functions.azurewebsites.net/api")]
        [InlineData("~functions.azurewebsites.net", "~functions.azurewebsites.net")]
        [InlineData("/api/HttpTrigger", "/api/HttpTrigger")]
        public void SanitizeUrlTest(string origin, string expected)
        {
            string result = ScmSiteUrlHelper.SanitizeUrl(origin);
            Assert.Equal(expected, result);
        }
    }
}
