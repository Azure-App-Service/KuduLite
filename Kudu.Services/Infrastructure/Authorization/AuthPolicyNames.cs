namespace Kudu.Services.Infrastructure.Authorization
{
    /// <summary>
    /// Authorization Policies that can be applied to the endpoints
    /// </summary>
    public static class AuthPolicyNames
    {
        /// <summary>
        /// Only requests with "x-ms-site-restricted-token" can access the endpoint
        /// </summary>
        public const string AdminAuthLevel = "AuthLevelAdmin";

        /// <summary>
        /// When running on Linux Consumption, only requests with "x-ms-site-restricted-token" can access the endpoint.
        /// No restriction when running on App Service.
        /// </summary>
        public const string LinuxConsumptionRestriction = "LinuxConsumptionRestriction";
    }
}
