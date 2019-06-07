using Kudu.Services.LinuxConsumptionInstanceAdmin;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Kudu.Tests.LinuxConsumptionInstanceAdmin
{
    public class LinuxConsumptionRouteMiddlewareTests
    {
        HttpContext _httpContext;
        LinuxConsumptionRouteMiddleware _middleware;

        public LinuxConsumptionRouteMiddlewareTests()
        {
            _httpContext = new DefaultHttpContext();
            _middleware = new LinuxConsumptionRouteMiddleware((context) => {
                return Task.CompletedTask;
            });
        }

        [Fact]
        public void AllowExactlyMatched()
        {
            _httpContext.Request.Path = "/api/zipdeploy";
            _middleware.Invoke(_httpContext).Wait();
            Assert.Equal<int>(200, _httpContext.Response.StatusCode);
        }

        [Fact]
        public void AllowQueryParameter()
        {
            _httpContext.Request.Path = "/api/zipdeploy";
            _httpContext.Request.QueryString = new QueryString("?querystring=context");
            _middleware.Invoke(_httpContext).Wait();
            Assert.Equal<int>(200, _httpContext.Response.StatusCode);
        }

        [Fact]
        public void AllowTrailingSlash()
        {
            _httpContext.Request.Path = "/api/zipdeploy/";
            _middleware.Invoke(_httpContext).Wait();
            Assert.Equal<int>(200, _httpContext.Response.StatusCode);
        }

        [Fact]
        public void MatchShouldBeCaseInvariant()
        {
            _httpContext.Request.Path = "/api/ZipDeploy";
            _middleware.Invoke(_httpContext).Wait();
            Assert.Equal<int>(200, _httpContext.Response.StatusCode);
        }

        [Fact]
        public void AllowChildRoute()
        {
            _httpContext.Request.Path = "/admin/instance";
            _middleware.Invoke(_httpContext).Wait();
            Assert.Equal<int>(200, _httpContext.Response.StatusCode);

            _httpContext.Request.Path = "/admin/instance/info";
            _middleware.Invoke(_httpContext).Wait();
            Assert.Equal<int>(200, _httpContext.Response.StatusCode);
        }

        [Fact]
        public void DisallowParentRoute()
        {
            _httpContext.Request.Path = "/admin";
            _middleware.Invoke(_httpContext).Wait();
            Assert.Equal<int>(404, _httpContext.Response.StatusCode);
        }

        [Fact]
        public void DisallowUnwhitelistedRoute()
        {
            _httpContext.Request.Path = "/admin/notinstance";
            _middleware.Invoke(_httpContext).Wait();
            Assert.Equal<int>(404, _httpContext.Response.StatusCode);
        }
    }
}
