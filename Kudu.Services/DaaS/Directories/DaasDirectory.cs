using System;

namespace Kudu.Services.Performance
{
    internal class DaasDirectory
    {
        protected static readonly string daasPath = Environment.ExpandEnvironmentVariables(@"%HOME%\Data\DaaS");
        protected static readonly string daasRelativePath = @"/Data/DaaS";
    }
}