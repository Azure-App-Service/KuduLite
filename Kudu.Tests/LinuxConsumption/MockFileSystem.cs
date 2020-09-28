using System;
using System.IO.Abstractions;
using Kudu.Core.Infrastructure;

namespace Kudu.Tests.LinuxConsumption
{
    public class MockFileSystem : IDisposable
    {
        private readonly IFileSystem _previous;

        public MockFileSystem(IFileSystem fileSystem)
        {
            _previous = FileSystemHelpers.Instance;
            FileSystemHelpers.Instance = fileSystem;
        }

        public void Dispose()
        {
            FileSystemHelpers.Instance = _previous;
        }
    }
}
