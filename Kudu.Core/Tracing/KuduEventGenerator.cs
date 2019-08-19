using Kudu.Core.Helpers;

namespace Kudu.Core.Tracing
{
    public class KuduEventGenerator
    {
        private static IKuduEventGenerator _eventGenerator = null;

        public static IKuduEventGenerator Log()
        {
            // Linux Consumptions only
            bool isLinuxContainer = !string.IsNullOrEmpty(Environment.ContainerName);
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
                        _eventGenerator = new DefaultKuduEventGenerator();
                    }
                }
            }
            return _eventGenerator;
        }
    }
}
