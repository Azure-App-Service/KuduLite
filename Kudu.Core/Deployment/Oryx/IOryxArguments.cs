using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Deployment.Oryx
{
    interface IOryxArguments
    {
        bool RunOryxBuild { get; set; }

        BuildOptimizationsFlags Flags { get; set; }

        string GenerateOryxBuildCommand(DeploymentContext context);
    }
}
