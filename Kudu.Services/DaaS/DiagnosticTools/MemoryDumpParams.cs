namespace Kudu.Services.Performance
{
    internal class MemoryDumpParams
    {
        internal int ProcessId { get; set; }
        internal string DumpType { get; set; } = "Mini";

        internal MemoryDumpParams(string diagnoserParams)
        {
            foreach(var param in diagnoserParams.Split(";"))
            {
                var singleParams = param.Split("=");
                if (singleParams.Length !=  2)
                {
                    continue;
                }

                if (singleParams[0] == "DumpType")
                {
                    this.DumpType = singleParams[1];
                }
            }
        }
    }
}
