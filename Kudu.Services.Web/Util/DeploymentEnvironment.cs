using System.IO;
using Kudu.Core;
using Kudu.Core.Deployment;

namespace Kudu.Services.Web.Services
{
    public abstract class DeploymentEnvironment : IDeploymentEnvironment
    {
        private readonly IEnvironment _environment;

        protected DeploymentEnvironment(IEnvironment environment)
        {
            _environment = environment;
        }

        public string ExePath => _environment.KuduConsoleFullPath;

        public string ApplicationPath => _environment.SiteRootPath;

        // CORE TODO What is this?
        public string MSBuildExtensionsPath => Path.Combine(System.AppContext.BaseDirectory, "msbuild");
    }
}