using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Deployment
{
    public enum BuildFlags
    {
        None = 0,
        UseTmpDirectory = 1,
        UseExpressBuild = 2
    }

    public class BuildFlagsHelper
    {
        public static BuildFlags Parse(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return BuildFlags.None;
            }

            try
            {
                var result = (BuildFlags)Enum.Parse(typeof(BuildFlags), value);
                return result;
            }
            catch (Exception)
            {
                return BuildFlags.None;
            }
        }
    }
}
