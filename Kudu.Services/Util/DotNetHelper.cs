using System;
using Kudu.Core.Helpers;

namespace Kudu.Services.Performance
{
    /// <summary>
    /// Helper class to check if this is a .NET Core blessed image
    /// </summary>
    public class DotNetHelper
    {
        private const string DotnetMonitorPort = "50051";

        /// <summary>
        /// Returns TRUE if the container is running a .NET Core blessed image and
        /// if the WEBSITE_USE_DOTNET_MONITOR is set to TRUE
        /// </summary>
        /// <returns></returns>
        public static bool IsDotNetMonitorEnabled()
        {
            if (OSDetector.IsOnWindows())
            {
                return true;
            }

            string val = Environment.GetEnvironmentVariable(Constants.DotNetMonitorEnabled);
            if (!string.IsNullOrWhiteSpace(val))
            {
                string stack = Environment.GetEnvironmentVariable(Constants.FrameworkSetting);
                if (!string.IsNullOrWhiteSpace(stack))
                {
                    return val.Equals("true", StringComparison.OrdinalIgnoreCase)
                        && stack.Equals("DOTNETCORE", StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the address of the Dotnet monitor server
        /// </summary>
        /// <returns></returns>
        public static string GetDotNetMonitorAddress()
        {
            if (OSDetector.IsOnWindows())
            {
                return "https://localhost:52323";
            }

            var ipAddress = GetIpAddress();
            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                return $"http://{ipAddress}:{DotnetMonitorPort}";
            }

            return string.Empty;
        }

        private static string GetIpAddress()
        {
            try
            {
                string ipAddress = System.IO.File.ReadAllText(Constants.AppServiceTempPath + Environment.GetEnvironmentVariable(Constants.AzureWebsiteRoleInstanceId));
                if (ipAddress != null)
                {
                    if (ipAddress.Contains(':'))
                    {
                        string[] ipAddrPortStr = ipAddress.Split(":");
                        return ipAddrPortStr[0];
                    }
                    else
                    {
                        return ipAddress;
                    }
                }
            }
            catch (Exception)
            {
            }

            return string.Empty;
        }
    }
}
