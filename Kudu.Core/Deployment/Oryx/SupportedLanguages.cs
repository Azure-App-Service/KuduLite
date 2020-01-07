using System;
using System.Collections.Generic;

namespace Kudu.Core.Deployment.Oryx
{
    public enum Framework
    {
        None,
        NodeJs,
        Python,
        DotNETCore,
        PHP,
        Go,
        Ruby
    }

    public class SupportedFrameworks
    {
        public static Framework ParseLanguage(string value)
        {
            if (value.StartsWith("NODE", StringComparison.OrdinalIgnoreCase))
            {
                return Framework.NodeJs;
            }
            else if (value.StartsWith("PYTHON", StringComparison.OrdinalIgnoreCase))
            {
                return Framework.Python;
            }
            else if (value.StartsWith("DOTNETCORE", StringComparison.OrdinalIgnoreCase))
            {
                return Framework.DotNETCore;
            }
            else if (value.StartsWith("PHP", StringComparison.OrdinalIgnoreCase))
            {
                return Framework.PHP;
            }
            else if (value.StartsWith("GO", StringComparison.OrdinalIgnoreCase))
            {
                return Framework.Go;
            }
            else if (value.StartsWith("RUBY", StringComparison.OrdinalIgnoreCase))
            {
                return Framework.Ruby;
            }

            return Framework.None;
        }
    }
}
