using System;

namespace Kudu.Core.Deployment
{
    public class ProgressLogger : ILogger
    {
        private readonly string _id;
        private readonly IDeploymentStatusManager _status;
        private readonly ILogger _innerLogger;
        private readonly IEnvironment _environment;

        public ProgressLogger(string id, IDeploymentStatusManager status, ILogger innerLogger, IEnvironment environment)
        {
            _id = id;
            _status = status;
            _innerLogger = innerLogger;
            _environment = environment;
        }

        public ILogger Log(string value, LogEntryType type)
        {
            IDeploymentStatusFile statusFile = _status.Open(_id, _environment);
            if (statusFile != null)
            {
                statusFile.UpdateProgress(value);
            }

            // No need to wrap this as we only support top-level progress
            return _innerLogger.Log(value, type);
        }
    }
}