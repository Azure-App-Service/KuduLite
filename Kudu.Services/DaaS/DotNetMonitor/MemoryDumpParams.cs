namespace Kudu.Services.DaaS
{
    internal class MemoryDumpParams
    {
        internal int ProcessId { get; set; }
        internal string DumpType { get; set; } = "Mini";
        internal MemoryDumpParams(string toolParams)
        {
            if (string.IsNullOrWhiteSpace(toolParams))
            {
                return;
            }
            
            foreach(var param in toolParams.Split(";"))
            {
                var singleParams = param.Split("=");
                if (singleParams.Length !=  2)
                {
                    continue;
                }

                if (singleParams[0] == "DumpType")
                {
                    DumpType = singleParams[1];
                }
            }
        }
    }
}
