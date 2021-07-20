namespace Kudu.Services.DaaS
{
    internal class ClrTraceParams
    {
        public int DurationSeconds { get; set; } = 60;
        public string TraceProfile { get; set; }

        internal ClrTraceParams(string toolParams)
        {
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
                        this.DurationSeconds = duration;
                    }
                }

                if (singleParams[0] == "TraceProfile")
                {
                    this.TraceProfile = singleParams[1];
                }
            }
        }
    }
}