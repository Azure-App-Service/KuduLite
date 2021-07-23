using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Services.DaaS
{
    internal class TaskAndCancellationToken
    {
        internal Task UnderlyingTask { get; set; }
        internal CancellationTokenSource CancellationTokenSource { get; set; }
    }
}
