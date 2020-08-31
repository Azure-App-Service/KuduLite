using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Contracts.Deployment
{
    public class DeployStatusApiResult
    {
        public DeployStatusApiResult(int DeploymentStatus, int DeploymentStatusInt, string DeploymentId)
        {
            this.DeploymentStatus = DeploymentStatus;
            this.DeploymentStatusInt = DeploymentStatusInt;
            this.DeploymentId = DeploymentId;
        }
        public int DeploymentStatus { get; set; }

        public int DeploymentStatusInt { get; set; }

        public string DeploymentId { get; set; }
    }
}
