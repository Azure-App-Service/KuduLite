using System.Collections.Generic;

namespace Kudu.Services.Performance
{
    public class ActiveInstance
    {
        public string Name { get; set; }
        public List<LogFile> Logs { get; set; }
        public Status Status { get; set; }

        public ActiveInstance()
        {
            Status = Status.Active;
            Logs = new List<LogFile>();
        }

        public ActiveInstance(string instanceName)
        {
            Name = instanceName;
            Status = Status.Active;
            Logs = new List<LogFile>();
        }
    }
}