using System;
using System.IO;

namespace Kudu.Services.Performance
{
    internal class DaasDirectory
    {
        protected static readonly string daasPath = Path.Combine(Environment.ExpandEnvironmentVariables(@"%HOME%"), "Data", "DaaS");
        protected static readonly string daasRelativePath = @"/Data/DaaS";
    }
}