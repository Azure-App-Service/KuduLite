namespace Kudu.Services.Performance
{
    public class LogFile
    {
        public string Name { get; set; }
        public string Instance { get; set; }
        public string FullPath { get; set; }
        public string RelativePath { get; set; }
        public long Size { get; set; }
    }
}