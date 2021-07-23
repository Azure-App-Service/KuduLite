using System.Collections.Generic;

namespace Kudu.Services.DaaS
{
    public class ActiveInstance
    {
        public string Name { get; set; }
        public List<LogFile> Logs { get; set; } = new List<LogFile>();
        public List<string> Errors { get; set; } = new List<string>();
        public Status Status { get; set; }
        public ActiveInstance()
        {
            Status = Status.Active;
        }

        public ActiveInstance(string instanceName)
        {
            Name = instanceName;
            Status = Status.Active;
        }
    }
}