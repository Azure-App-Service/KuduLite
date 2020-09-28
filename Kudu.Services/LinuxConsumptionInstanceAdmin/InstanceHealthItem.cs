namespace Kudu.Services.LinuxConsumptionInstanceAdmin
{
    /// <summary>
    /// Individual health status item
    /// </summary>
    public class InstanceHealthItem
    {
        /// <summary>
        /// Name of the component
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Success/failure
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Additional details
        /// </summary>
        public string Message { get; set; }
    }
}