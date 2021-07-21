using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;

namespace Kudu.Services.DaaS
{
    internal class SessionLinuxLockFile : LinuxLockFile
    {
        readonly string _lockFilePath;
        public SessionLinuxLockFile(string path, ITraceFactory traceFactory, bool ensureLock = false) : base(path, traceFactory, ensureLock)
        {
            _lockFilePath = path;
        }

        public override void Release()
        {
            base.Release();
            FileSystemHelpers.DeleteFileSafe(_lockFilePath);
        }
    }
}
