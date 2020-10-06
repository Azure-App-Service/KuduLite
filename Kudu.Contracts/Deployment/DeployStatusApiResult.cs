using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Contracts.Deployment
{
    public class DeployStatusApiResult
    {
        public DeployStatusApiResult(string DeploymentStatus, string DeploymentId)
        {
            this.DeploymentStatus = DeploymentStatus;
            this.DeploymentId = DeploymentId;
        }

        public string DeploymentStatus { get; set; }

        public string DeploymentId { get; set; }
    }
}
