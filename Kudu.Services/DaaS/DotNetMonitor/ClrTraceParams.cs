namespace Kudu.Services.DaaS
{
    internal class ClrTraceParams
    {
        internal int DurationSeconds { get; set; } = 60;
        internal string TraceProfile { get; set; } = "Cpu,Http,Metrics";

        internal ClrTraceParams(string toolParams)
        {
            if (string.IsNullOrWhiteSpace(toolParams))
            {
                return;
            }

            foreach (var param in toolParams.Split(";"))
            {
                var singleParams = param.Split("=");
                if (singleParams.Length != 2)
                {
                    continue;
                }

                if (singleParams[0] == "DurationSeconds")
                {
                    if (int.TryParse(singleParams[1], out int duration))
                    {
                        DurationSeconds = duration;
                    }
                }

                if (singleParams[0] == "TraceProfile")
                {
                    TraceProfile = singleParams[1];
                }
            }
        }
    }
}