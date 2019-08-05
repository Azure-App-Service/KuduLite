namespace Kudu.Core.Tracing
{
    public class KuduEventGenerator
    {
        private static IKuduEventGenerator _eventGenerator = null;

        public static IKuduEventGenerator Log()
        {
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
                    _eventGenerator = new DefaultKuduEventGenerator();
                }
            }
            return _eventGenerator;
        }
    }
}
