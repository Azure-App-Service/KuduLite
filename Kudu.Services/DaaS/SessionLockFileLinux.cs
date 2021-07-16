using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;

namespace Kudu.Services.Performance
{
    internal class SessionLockFileLinux : LinuxLockFile
    {
        public SessionLockFileLinux(string path, ITraceFactory traceFactory, bool ensureLock = false) : base(path, traceFactory, ensureLock)
        {
            
        }

        public override void Release()
        {
            base.Release();
            FileSystemHelpers.DeleteFileSafe(_lockFile);
        }
    }
}
