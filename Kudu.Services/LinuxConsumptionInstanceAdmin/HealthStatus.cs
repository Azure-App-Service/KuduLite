using System.Collections.Generic;

namespace Kudu.Services.LinuxConsumptionInstanceAdmin
{
    /// <summary>
    /// Health status response
    /// </summary>
    public class InstanceHealth
    {
        /// <summary>
        /// Collection of health status per component
        /// </summary>
        public List<InstanceHealthItem> Items;
    }
}
