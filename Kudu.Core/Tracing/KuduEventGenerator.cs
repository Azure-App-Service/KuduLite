using Kudu.Core.Helpers;
using Kudu.Core.LinuxConsumption;

namespace Kudu.Core.Tracing
{
    public class KuduEventGenerator
    {
        private static IKuduEventGenerator _eventGenerator = null;

        public static IKuduEventGenerator Log(ISystemEnvironment systemEnvironment = null)
        {
            string containerName = systemEnvironment != null
                ? systemEnvironment.GetEnvironmentVariable(Constants.ContainerName)
                : Environment.ContainerName;

            // Linux Consumptions only
            bool isLinuxContainer = !string.IsNullOrEmpty(containerName);
            if (isLinuxContainer)
            {
                if (_eventGenerator == null)
                {
                    _eventGenerator = new LinuxContainerEventGenerator();
                }            
            }
            else
            {
                if (_eventGenerator == null)
                {
                    // Generate ETW events when running on windows
                    if (OSDetector.IsOnWindows())
                    {
                        _eventGenerator = new DefaultKuduEventGenerator();
                    }
                    else
                    {
                        _eventGenerator = new Log4NetEventGenerator();
                    }
                }
            }
            return _eventGenerator;
        }
    }
}
