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
        UseExpressBuild
    }

    public class BuildFlagsHelper
    {
        public static BuildOptimizationsFlags Parse(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return BuildOptimizationsFlags.None;
            }

            try
            {
                var result = (BuildOptimizationsFlags)Enum.Parse(typeof(BuildOptimizationsFlags), value);
                return result;
            }
            catch (Exception)
            {
                return BuildOptimizationsFlags.None;
            }
        }
    }
}
