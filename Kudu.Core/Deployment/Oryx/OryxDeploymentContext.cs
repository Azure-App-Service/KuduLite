
namespace Kudu.Core.Deployment.Oryx
{
    class OryxDeploymentContext
    {
        public bool RunOryxBuild { get; set; }

        public BuildOptimizationsFlags Flags { get; set; }

        public Framework Language { get; set; }

        public string Version { get; set; }

        public string PublishFolder { get; set; }

        public string VirtualEnv { get; set; }

        public bool SkipKuduSync { get; set; }

    }
}
