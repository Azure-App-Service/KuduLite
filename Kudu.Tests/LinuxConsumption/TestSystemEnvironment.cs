using System.Collections.Generic;
using Kudu.Core.LinuxConsumption;

namespace Kudu.Tests.LinuxConsumption
{
    public class TestSystemEnvironment : ISystemEnvironment
    {
        private readonly Dictionary<string, string> _environment;

        public TestSystemEnvironment(Dictionary<string, string> environment = null)
        {
            _environment = environment ?? new Dictionary<string, string>();
        }

        public string GetEnvironmentVariable(string name)
        {
            return _environment[name];
        }

        public void SetEnvironmentVariable(string name, string value)
        {
            _environment[name] = value;
        }
    }
}
