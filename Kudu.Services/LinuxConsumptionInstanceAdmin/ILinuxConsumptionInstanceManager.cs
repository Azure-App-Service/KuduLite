using System.Collections.Generic;
using System.Threading.Tasks;
using Kudu.Services.Models;

namespace Kudu.Services.LinuxConsumptionInstanceAdmin
{
    /// <summary>
    /// Instance manager interface for modifying the behaviour in current instance
    /// </summary>
    public interface ILinuxConsumptionInstanceManager
    {
        /// <summary>
        /// Get an overview information of current instance health status
        /// </summary>
        /// <returns></returns>
        IDictionary<string, string> GetInstanceInfo();

        /// <summary>
        /// Validate if the assignment context matches the requirement for current instance's specialization
        /// </summary>
        /// <param name="assignmentContext">Contains site information such as environment variables</param>
        /// <returns>Message on error, otherwise, null.</returns>
        Task<string> ValidateContext(HostAssignmentContext assignmentContext);

        /// <summary>
        /// Apply assignment context and start specialization for current instance
        /// </summary>
        /// <param name="assignmentContext">Contains site information such as environment variables</param>
        /// <returns>True on success.</returns>
        bool StartAssignment(HostAssignmentContext assignmentContext);
    }
}
