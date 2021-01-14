using System;
using Kudu.Core.LinuxConsumption;

namespace Kudu.Tests.LinuxConsumption
{
    public class TestFileSystemPathProvider : IFileSystemPathProvider
    {
        private static readonly Lazy<TestFileSystemPathProvider> _instance = new Lazy<TestFileSystemPathProvider>(CreateInstance);

        private TestFileSystemPathProvider()
        {
        }

        public static TestFileSystemPathProvider Instance => _instance.Value;

        private static TestFileSystemPathProvider CreateInstance()
        {
            return new TestFileSystemPathProvider();
        }

        public bool TryGetDeploymentsPath(out string path)
        {
            path = null;
            return false;
        }
    }
}
