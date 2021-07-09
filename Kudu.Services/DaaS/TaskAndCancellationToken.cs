using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Services.Performance
{
    public class TaskAndCancellationToken
    {
        public Task UnderlyingTask { get; set; }
        public CancellationTokenSource CancellationSource { get; set; }
    }
}
