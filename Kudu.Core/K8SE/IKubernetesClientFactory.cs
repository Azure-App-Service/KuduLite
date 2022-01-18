using k8s;

namespace Kudu.Core.K8SE
{
    public interface IKubernetesClientFactory
    {
        IKubernetes CreateClient();
    }
}