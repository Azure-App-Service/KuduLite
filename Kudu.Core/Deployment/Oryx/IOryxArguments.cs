using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Deployment.Oryx
{
    public interface IOryxArguments
    {
        bool RunOryxBuild { get; set; }

        BuildOptimizationsFlags Flags { get; set; }

        bool SkipKuduSync { get; set; }

        string GenerateOryxBuildCommand(DeploymentContext context);

        string Version { get; set; }

        Framework Language { get; set; }

        string PublishFolder { get; set; }
        string VirtualEnv { get; set; }
    }
}
