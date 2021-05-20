using System;
using Kudu.Core.Helpers;

namespace Kudu.Services.Performance
{
    /// <summary>
    /// Helper class to check if this is a .NET Core blessed image
    /// </summary>
    public class DotNetHelper
    {
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

            string val = Environment.GetEnvironmentVariable("WEBSITE_USE_DOTNET_MONITOR");
            if (!string.IsNullOrWhiteSpace(val))
            {
                string stack = Environment.GetEnvironmentVariable("WEBSITE_STACK");
                if (!string.IsNullOrWhiteSpace(stack))
                {
                    return val.Equals("true", StringComparison.OrdinalIgnoreCase)
                        && stack.Equals("DOTNETCORE", StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }
    }
}
