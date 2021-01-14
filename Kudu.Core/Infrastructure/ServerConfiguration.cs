using System;
using Kudu.Core.LinuxConsumption;

namespace Kudu.Core.Infrastructure
{
    public class ServerConfiguration : IServerConfiguration
    {
        private readonly ISystemEnvironment _environment;
        private string _applicationName;

        public ServerConfiguration(ISystemEnvironment environment)
        {
            _environment = environment;
        }

        public string ApplicationName
        {
            get
            {
                // No caching on consumption sku since the container starts in placeholder mode
                if (_environment.IsOnLinuxConsumption())
                {
                    return GetApplicationName(_environment);
                }

                if (_applicationName == null)
                {
                    _applicationName = GetApplicationName(_environment);
                }
                return _applicationName;
            }
        }

        public string GitServerRoot
        {
            get
            {
                if (String.IsNullOrEmpty(ApplicationName))
                {
                    return "git";
                }
                return ApplicationName + ".git";
            }
        }

        // todo: Make systemEnvironment a mandatory parameter.
        public static string GetApplicationName(ISystemEnvironment systemEnvironment = null)
        {
            var applicationName = systemEnvironment != null
                ? systemEnvironment.GetEnvironmentVariable(Constants.WebsiteSiteName)
                : System.Environment.GetEnvironmentVariable(Constants.WebsiteSiteName);

            if (!string.IsNullOrEmpty(applicationName))
            {
                // Yank everything after the first underscore to work around
                // a slot issue where WEBSITE_SITE_NAME gets set incorrectly
                int underscoreIndex = applicationName.IndexOf('_');
                if (underscoreIndex > 0)
                {
                    applicationName = applicationName.Substring(0, underscoreIndex);
                }

                return applicationName;
            }

            applicationName = systemEnvironment != null
                ? systemEnvironment.GetEnvironmentVariable("APP_POOL_ID")
                : System.Environment.GetEnvironmentVariable("APP_POOL_ID");

            if (applicationName != null)
            {
                return applicationName;
            }

            return String.Empty;
        }
    }
}
