using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Deployment
{
    public enum BuildOptimizationsFlags
    {
        Off,
        None,
        CompressModules,
        UseExpressBuild,
        UseTempDirectory,
        UseRFPV2
    }

    public class BuildFlagsHelper
    {
        public static BuildOptimizationsFlags Parse(string value, BuildOptimizationsFlags defaultVal = BuildOptimizationsFlags.None)
        {
            if (string.IsNullOrEmpty(value))
            {
                return defaultVal;
            }

            try
            {
                var result = (BuildOptimizationsFlags)Enum.Parse(typeof(BuildOptimizationsFlags), value);
                return result;
            }
            catch (Exception)
            {
                return defaultVal;
            }
        }
    }
}
