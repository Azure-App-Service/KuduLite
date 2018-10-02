using Kudu.Core.Deployment;

namespace Kudu.Console.Services
{
    public class ConsoleLogger : ILogger
    {
        public bool HasErrors { get; set; }

        public ILogger Log(string value, LogEntryType type)
        {
            if (type == LogEntryType.Error)
            {
                HasErrors = true;
                //REMOVE TODO
                System.Console.WriteLine("\n\n\n\n\nKUDU CONSOLE ERROR : "+value+"\n\n\n\n\n\n\n");
                System.Console.Error.WriteLine(value);
            }
            else
            {
                System.Console.WriteLine(value);
            }

            return NullLogger.Instance;
        }
    }
}
