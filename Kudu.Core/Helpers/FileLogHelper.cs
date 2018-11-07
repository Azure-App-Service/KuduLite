using System;

namespace Kudu.Core.Helpers
{
    public static class FileLogHelper
    {
        public static void Log(string message)
        {
            string output = string.Format("{0}: {1}\n", DateTime.UtcNow, message);
            System.IO.File.AppendAllText("/tmp/filelogs.txt", output);
        }
    }
}
