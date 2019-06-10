using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Kudu.Services.Infrastructure.Authentication;
using Kudu.Core.Helpers;
using Kudu.Contracts.Settings;
using Kudu.Services.Infrastructure.Authorization;
using Kudu.Tests;

namespace Kudu.Tests.Services.Infrastructure.Authentication
{
    [Collection("MockedEnvironmentVariablesCollection")]
    public class ArmAuthenticationHandlerTests
    {
        private static DefaultHttpContext GetContext()
        {
            var services = new ServiceCollection().AddLogging();
            services.AddAuthentication(o =>
            {
                o.DefaultScheme = ArmAuthenticationDefaults.AuthenticationScheme;
            })
            .AddArmToken();

            var sp = services.BuildServiceProvider();
            var context = new DefaultHttpContext
            {
                RequestServices = sp
            };
            return context;
        }

        [Fact]
        public async Task AuthenticateAsync_WithoutToken_DoesNotAuthenticate()
        {
            // Arrange
            DefaultHttpContext context = GetContext();

            // Act
            AuthenticateResult result = await context.AuthenticateAsync();

            // Assert
            Assert.True(result.None);
            Assert.Null(result.Failure);
            Assert.Null(result.Principal);
        }

        [Fact]
        public async Task AuthenticateAsync_WithToken_PerformsAuthentication_Using_WebSiteAuthEncryptionKey_IfAvailable()
        {
            var websiteAuthEncryptionKeyBytes = TestHelpers.GenerateKeyBytes();
            var containerEncryptionKeyBytes = TestHelpers.GenerateKeyBytes();

            var vars = new Dictionary<string, string>
            {
                { SettingsKeys.AuthEncryptionKey, TestHelpers.GenerateKeyHexString(websiteAuthEncryptionKeyBytes) },
                { SettingsKeys.ContainerEncryptionKey, TestHelpers.GenerateKeyHexString(containerEncryptionKeyBytes) }
            };

            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                // Arrange
                DefaultHttpContext context = GetContext();

                string token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(2), websiteAuthEncryptionKeyBytes);
                context.Request.Headers.Add(ArmAuthenticationHandler.ArmTokenHeaderName, token);

                // Act
                AuthenticateResult result = await context.AuthenticateAsync();

                // Assert
                Assert.True(result.Succeeded);
                Assert.True(result.Principal.Identity.IsAuthenticated);
                Assert.True(result.Principal.HasClaim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString()));
            }
        }

        [Fact]
        public async Task AuthenticateAsync_WithToken_PerformsAuthentication_Using_ContainerEncryptionKey_If_WebSiteAuthEncryptionKey_Not_Available()
        {
            var containerEncryptionKeyBytes = TestHelpers.GenerateKeyBytes();

            var vars = new Dictionary<string, string>
            {
                { SettingsKeys.AuthEncryptionKey, string.Empty },
                { SettingsKeys.ContainerEncryptionKey, TestHelpers.GenerateKeyHexString(containerEncryptionKeyBytes) }
            };

            using (var env = new TestScopedEnvironmentVariable(vars))
            {
                // Arrange
                DefaultHttpContext context = GetContext();

                string token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(2), containerEncryptionKeyBytes);
                context.Request.Headers.Add(ArmAuthenticationHandler.ArmTokenHeaderName, token);

                // Act
                AuthenticateResult result = await context.AuthenticateAsync();

                // Assert
                Assert.True(result.Succeeded);
                Assert.True(result.Principal.Identity.IsAuthenticated);
                Assert.True(result.Principal.HasClaim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString()));
            }
        }

        [Fact]
        public async Task AuthenticateAsync_WithToken_PerformsAuthentication()
        {
            using (new TestScopedEnvironmentVariable(SettingsKeys.AuthEncryptionKey, TestHelpers.GenerateKeyHexString()))
            {
                // Arrange
                DefaultHttpContext context = GetContext();

                string token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(2));
                context.Request.Headers.Add(ArmAuthenticationHandler.ArmTokenHeaderName, token);

                // Act
                AuthenticateResult result = await context.AuthenticateAsync();

                // Assert
                Assert.True(result.Succeeded);
                Assert.True(result.Principal.Identity.IsAuthenticated);
                Assert.True(result.Principal.HasClaim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Admin.ToString()));
            }
        }

        [Fact]
        public async Task AuthenticateAsync_WithInvalidToken_FailsAuthentication()
        {
            using (new TestScopedEnvironmentVariable(SettingsKeys.AuthEncryptionKey, TestHelpers.GenerateKeyHexString()))
            {
                // Arrange
                DefaultHttpContext context = GetContext();

                string token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(2));
                token = token.Substring(0, token.Length - 5);
                context.Request.Headers.Add(ArmAuthenticationHandler.ArmTokenHeaderName, token);

                // Act
                AuthenticateResult result = await context.AuthenticateAsync();

                // Assert
                Assert.False(result.Succeeded);
                Assert.NotNull(result.Failure);
                Assert.Null(result.Principal);
            }
        }

        [Fact]
        public async Task AuthenticateAsync_WithExpiredToken_FailsAuthentication()
        {
            using (new TestScopedEnvironmentVariable(SettingsKeys.AuthEncryptionKey, TestHelpers.GenerateKeyHexString()))
            {
                DefaultHttpContext context = GetContext();

                string token = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(-20));
                context.Request.Headers.Add(ArmAuthenticationHandler.ArmTokenHeaderName, token);

                // Act
                AuthenticateResult result = await context.AuthenticateAsync();

                // Assert
                Assert.False(result.Succeeded);
                Assert.NotNull(result.Failure);
                Assert.Null(result.Principal);
            }
        }
    }
}
