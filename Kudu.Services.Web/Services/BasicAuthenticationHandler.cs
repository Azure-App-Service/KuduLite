using k8s;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace Kudu.Services.Web.Services
{
    public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public static string PublishingProfileSecretPrefix = "PUBLISHING-PROFILE-";

        public BasicAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock)
            : base(options, logger , encoder, clock)
        {
        }

        public static KuduMemCache<string> memCache = new KuduMemCache<string>();

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return AuthenticateResult.Fail("Missing Authorization Header");
            }

            try
            {
                var authHeader = AuthenticationHeaderValue.Parse(Request.Headers["Authorization"]);
                var credentialBytes = Convert.FromBase64String(authHeader.Parameter);
                var credentials = Encoding.UTF8.GetString(credentialBytes).Split(new[] { ':' }, 2);
                var username = credentials[0];
                var password = credentials[1];
                Console.WriteLine($"password supplied: {password}");
                if ((memCache.GetOrCreate(username, GetAuthenticationData)).Equals(password))
                {
                    var claims = new[] {
                        new Claim(ClaimTypes.NameIdentifier, username),
                        new Claim(ClaimTypes.Name, username),
                    };

                    var identity = new ClaimsIdentity(claims, Scheme.Name);
                    var principal = new ClaimsPrincipal(identity);
                    var ticket = new AuthenticationTicket(principal, Scheme.Name);
                    return AuthenticateResult.Success(ticket);
                }
                else
                {
                    return AuthenticateResult.Fail("Invalid Username or Password");
                }

            }
            catch
            {
                return AuthenticateResult.Fail("Invalid Authorization Header");
            }
        }


        public async Task<string> GetAuthenticationData(string username)
        {
            var config = KubernetesClientConfiguration.InClusterConfig();
            string password = "";

            // Use the config object to create a client.
            var client = new Kubernetes(config);
            try
            {
                Console.WriteLine("Retrieving Password: " + password);
                var secret = await client.ReadNamespacedSecretAsync($"{PublishingProfileSecretPrefix.ToLower()}{username.ToLower()}","k8seappspubpassword");
                Console.WriteLine("secret retrieved: ");
                password = System.Text.Encoding.UTF8.GetString(secret.Data["password"]);
                Console.WriteLine("Password retrieved: " + password);
            }
            catch (Microsoft.Rest.HttpOperationException httpOperationException)
            {
                var phrase = httpOperationException.Response.ReasonPhrase;
                var content = httpOperationException.Response.Content;
                Console.WriteLine("K8 Client errror");
                Console.WriteLine(phrase);
                Console.WriteLine(content);
            }
            return password;
        }
    }
}
