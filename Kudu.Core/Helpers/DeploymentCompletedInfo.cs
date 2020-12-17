using System;
using System.IO;
using Kudu.Core.Deployment;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Newtonsoft.Json;

namespace Kudu.Core.Helpers
{
    public class DeploymentCompletedInfo
    {
        public const string LatestDeploymentFile = "LatestDeployment.json";

        public string TimeStamp { get; set; }
        public string SiteName { get; set; }
        public string RequestId { get; set; }
        public string Kind { get; set; }
        public string Status { get; set; }
        public string Details { get; set; }

        private static IAnalytics _analytics;

        public static void Persist(string requestId, IDeploymentStatusFile status, IAnalytics analytics)
        {
            // signify the deployment is done by git push
            var kind = System.Environment.GetEnvironmentVariable(Constants.ScmDeploymentKind);
            if (string.IsNullOrEmpty(kind))
            {
                kind = status.Deployer;
            }
            _analytics = analytics;
            var serializedStatus = JsonConvert.SerializeObject(status, Formatting.Indented);
            Persist(status.SiteName, kind, requestId, status.Status.ToString(), serializedStatus);
        }

        public static void Persist(string siteName, string kind, string requestId, string status, string details)
        {
            var info = new DeploymentCompletedInfo
            {
                TimeStamp = $"{DateTime.UtcNow:s}Z",
                SiteName = siteName,
                Kind = kind,
                RequestId = requestId,
                Status = status,
                Details = details ?? string.Empty
            };

            try
            {
                var path = Path.Combine(System.Environment.ExpandEnvironmentVariables(@"%HOME%"), "site", "deployments");
                var file = Path.Combine(path, $"{Constants.LatestDeployment}.json");
                var content = JsonConvert.SerializeObject(info, Formatting.Indented);
                FileSystemHelpers.EnsureDirectory(path);

                // write deployment info to %home%\site\deployments\LatestDeployment.json
                OperationManager.Attempt(() => FileSystemHelpers.Instance.File.WriteAllText(file, content));

                _analytics.DeploymentCompleted(
                    info.SiteName,
                    info.Kind,
                    info.RequestId,
                    info.Status,
                    info.Details);
            }
            catch (Exception ex)
            {
                _analytics.UnexpectedException(ex);
            }
        }
    }
}