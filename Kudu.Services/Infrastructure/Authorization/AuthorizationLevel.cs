using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Services.Infrastructure.Authorization
{
    /// <summary>
    /// Levels used for distinguishing UserLevel auth against AdminLevel auth
    /// </summary>
    public enum AuthorizationLevel
    {
        /// <summary>
        /// Anyone who has access to KuduLite can call the endpoint
        /// </summary>
        Anonymous = 0,

        /// <summary>
        /// Only Antares hosting platform can call the endpoint (e.g. admin/instance/assign)
        /// </summary>
        Admin = 1
    }
}
