using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Services.DaaS
{
    public class TaskAndCancellationToken
    {
        public Task UnderlyingTask { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }
    }
}
