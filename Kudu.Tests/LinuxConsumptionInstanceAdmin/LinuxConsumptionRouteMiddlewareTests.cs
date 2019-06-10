using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Kudu.Services.Infrastructure.Authentication;
using Kudu.Services.LinuxConsumptionInstanceAdmin;
using Kudu.Contracts.Settings;
using Xunit;
using Kudu.Services.Infrastructure.Authorization;

namespace Kudu.Tests.LinuxConsumptionInstanceAdmin
{
    [Collection("MockedEnvironmentVariablesCollection")]
    public class LinuxConsumptionRouteMiddlewareTests
    {
        private LinuxConsumptionRouteMiddleware _middleware;
        private byte[] _websiteAuthEncryptionKey;
        private byte[] _containerEncryptionKey;
        private Dictionary<string, string> _environmentVariables;

        public LinuxConsumptionRouteMiddlewareTests()
        {
            _middleware = new LinuxConsumptionRouteMiddleware((context) =>
            {
                return Task.CompletedTask;
            });

            _websiteAuthEncryptionKey = TestHelpers.GenerateKeyBytes();
            _containerEncryptionKey = TestHelpers.GenerateKeyBytes();
            _environmentVariables = new Dictionary<string, string>
            {
                { SettingsKeys.AuthEncryptionKey, TestHelpers.GenerateKeyHexString(_websiteAuthEncryptionKey) },
                { SettingsKeys.ContainerEncryptionKey, TestHelpers.GenerateKeyHexString(_containerEncryptionKey) }
            };
        }

        private HttpContext GenerateHttpContext(DateTime? tokenExpiry = null, byte[] encryptionKey = null)
        {
            var services = new ServiceCollection().AddLogging();

            services.AddAuthentication(o =>
            {
                o.DefaultScheme = ArmAuthenticationDefaults.AuthenticationScheme;
            })
            .AddArmToken();

            services.AddAuthorization(o =>
            {
                o.AddPolicy(AuthPolicyNames.LinuxConsumptionRestriction, p =>
                {
                    p.AuthenticationSchemes.Add(ArmAuthenticationDefaults.AuthenticationScheme);
                    p.AddRequirements(new AuthLevelRequirement(AuthorizationLevel.Admin));
                });
            });

            var sp = services.BuildServiceProvider();
            var context = new DefaultHttpContext
            {
                RequestServices = sp
            };

            if (tokenExpiry != null)
            {
                string token = Kudu.Core.Helpers.SimpleWebTokenHelper.CreateToken(
                    (DateTime)tokenExpiry, encryptionKey ?? _websiteAuthEncryptionKey);
                context.Request.Headers.Add(ArmAuthenticationHandler.ArmTokenHeaderName, token);
            }
            return context;
        }

        [Fact]
        public void ParentRouteNotFound()
        {
            using (new TestScopedEnvironmentVariable(_environmentVariables))
            {
                HttpContext httpContext = GenerateHttpContext(DateTime.UtcNow.AddDays(1));
                httpContext.Request.Path = "/admin";
                _middleware.Invoke(httpContext).Wait();
                Assert.Equal<int>(404, httpContext.Response.StatusCode);
            }
        }

        [Fact]
        public void UnwhitelistedRouteNotFound()
        {
            using (new TestScopedEnvironmentVariable(_environmentVariables))
            {
                HttpContext httpContext = GenerateHttpContext(DateTime.UtcNow.AddDays(1));
                httpContext.Request.Path = "/admin/notinstance";
                _middleware.Invoke(httpContext).Wait();
                Assert.Equal<int>(404, httpContext.Response.StatusCode);
            }
        }

        [Fact]
        public void HomepageRouteNotFound()
        {
            using (new TestScopedEnvironmentVariable(_environmentVariables))
            {
                HttpContext httpContext = GenerateHttpContext(DateTime.UtcNow.AddDays(1));
                httpContext.Request.Path = "/";
                _middleware.Invoke(httpContext).Wait();
                Assert.Equal<int>(404, httpContext.Response.StatusCode);
            }
        }

        [Fact]
        public void ExactlyMatchedOk()
        {
            using (new TestScopedEnvironmentVariable(_environmentVariables))
            {
                HttpContext httpContext = GenerateHttpContext(DateTime.UtcNow.AddDays(1));
                httpContext.Request.Path = "/api/zipdeploy";
                _middleware.Invoke(httpContext).Wait();
                Assert.Equal<int>(200, httpContext.Response.StatusCode);
            }
        }

        [Fact]
        public void QueryParameterOk()
        {
            using (new TestScopedEnvironmentVariable(_environmentVariables))
            {
                HttpContext httpContext = GenerateHttpContext(DateTime.UtcNow.AddDays(1));
                httpContext.Request.Path = "/api/zipdeploy";
                httpContext.Request.QueryString = new QueryString("?querystring=context");
                _middleware.Invoke(httpContext).Wait();
                Assert.Equal<int>(200, httpContext.Response.StatusCode);
            }
        }

        [Fact]
        public void TrailingSlashOk()
        {
            using (new TestScopedEnvironmentVariable(_environmentVariables))
            {
                HttpContext httpContext = GenerateHttpContext(DateTime.UtcNow.AddDays(1));
                httpContext.Request.Path = "/api/zipdeploy/";
                _middleware.Invoke(httpContext).Wait();
                Assert.Equal<int>(200, httpContext.Response.StatusCode);
            }
        }

        [Fact]
        public void CaseInvariantMatchOk()
        {
            using (new TestScopedEnvironmentVariable(_environmentVariables))
            {
                HttpContext httpContext = GenerateHttpContext(DateTime.UtcNow.AddDays(1));
                httpContext.Request.Path = "/api/ZipDeploy";
                _middleware.Invoke(httpContext).Wait();
                Assert.Equal<int>(200, httpContext.Response.StatusCode);
            }
        }

        [Fact]
        public void ChildRouteOk()
        {
            using (new TestScopedEnvironmentVariable(_environmentVariables))
            {
                HttpContext httpContext = GenerateHttpContext(DateTime.UtcNow.AddDays(1));
                httpContext.Request.Path = "/admin/instance";
                _middleware.Invoke(httpContext).Wait();
                Assert.Equal<int>(200, httpContext.Response.StatusCode);

                httpContext.Request.Path = "/admin/instance/info";
                _middleware.Invoke(httpContext).Wait();
                Assert.Equal<int>(200, httpContext.Response.StatusCode);
            }
        }

        [Fact]
        public void ExpiredTokenUnauthorized()
        {
            using (new TestScopedEnvironmentVariable(_environmentVariables))
            {
                HttpContext httpContext = GenerateHttpContext(DateTime.UtcNow.AddDays(-1));
                httpContext.Request.Path = "/admin/instance/info";
                _middleware.Invoke(httpContext).Wait();
                Assert.Equal<int>(401, httpContext.Response.StatusCode);
            }
        }

        [Fact]
        public void NoEncyptionKeyUnauthorized()
        {
            HttpContext httpContext = GenerateHttpContext(DateTime.UtcNow.AddDays(1));
            httpContext.Request.Path = "/api/zipdeploy";
            _middleware.Invoke(httpContext).Wait();
            Assert.Equal<int>(401, httpContext.Response.StatusCode);
        }

        [Fact]
        public void UseWebsiteEncryptionKeyOk()
        {
            using (new TestScopedEnvironmentVariable(
                SettingsKeys.AuthEncryptionKey, TestHelpers.GenerateKeyHexString(_websiteAuthEncryptionKey)))
            {
                HttpContext httpContext = GenerateHttpContext(DateTime.UtcNow.AddDays(1));
                httpContext.Request.Path = "/api/zipdeploy";
                _middleware.Invoke(httpContext).Wait();
                Assert.Equal<int>(200, httpContext.Response.StatusCode);
            }
        }

        [Fact]
        public void UseContainerEncryptionKeyOk()
        {
            using (new TestScopedEnvironmentVariable(
                SettingsKeys.ContainerEncryptionKey, TestHelpers.GenerateKeyHexString(_containerEncryptionKey)))
            {
                HttpContext httpContext = GenerateHttpContext(DateTime.UtcNow.AddDays(1), _containerEncryptionKey);
                httpContext.Request.Path = "/api/zipdeploy";
                _middleware.Invoke(httpContext).Wait();
                Assert.Equal<int>(200, httpContext.Response.StatusCode);
            }
        }
    }
}
