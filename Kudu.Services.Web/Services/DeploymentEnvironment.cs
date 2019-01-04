using System;
using System.IO;
using Kudu.Core;
using Kudu.Core.Deployment;

namespace Kudu.Services.Web.Services
{
    public class DeploymentEnvironment : IDeploymentEnvironment
    {
        private readonly IEnvironment _environment;

        public DeploymentEnvironment(IEnvironment environment)
        {
            _environment = environment;
        }

        public string ExePath => _environment.KuduConsoleFullPath;

        public string ApplicationPath => _environment.SiteRootPath;

        // CORE TODO: Where is this used?
        public string MSBuildExtensionsPath => Path.Combine(AppContext.BaseDirectory, "msbuild");
    }
}