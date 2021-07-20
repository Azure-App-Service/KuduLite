namespace Kudu.Services.DaaS
{
    class DotNetMonitorProcessResponse
    {
        public int pid { get; set; }
        public string uid { get; set; }
        public string name { get; set; }
        public string commandLine { get; set; }
        public string operatingSystem { get; set; }
        public string processArchitecture { get; set; }
    }
}
