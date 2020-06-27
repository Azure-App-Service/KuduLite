using Kudu.Contracts;
using Kudu.Services.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Threading.Tasks;
using Kudu.Core.Infrastructure;
using Kudu.Core.LinuxConsumption;
using Kudu.Core.Tracing;

namespace Kudu.Services.LinuxConsumptionInstanceAdmin
{
    /// <summary>
    /// Used for configuring KuduLite instance when it is running in Linux Consumption Service Fabric Mesh
    /// </summary>
    public class LinuxConsumptionInstanceManager : ILinuxConsumptionInstanceManager
    {
        private static readonly object _assignmentLock = new object();
        private static HostAssignmentContext _assignmentContext;

        private readonly ILinuxConsumptionEnvironment _linuxConsumptionEnv;
        private readonly IMeshPersistentFileSystem _meshPersistentFileSystem;

        /// <summary>
        /// Create a manager to specialize KuduLite when it is running in Service Fabric Mesh
        /// </summary>
        /// <param name="linuxConsumptionEnv">Environment variables</param>
        /// <param name="meshPersistentFileSystem">Provides persistent file storage</param>
        public LinuxConsumptionInstanceManager(ILinuxConsumptionEnvironment linuxConsumptionEnv, IMeshPersistentFileSystem meshPersistentFileSystem)
        {
            _linuxConsumptionEnv = linuxConsumptionEnv;
            _meshPersistentFileSystem = meshPersistentFileSystem;
        }

        public IDictionary<string, string> GetInstanceInfo()
        {
            var result = new Dictionary<string, string>();
            foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                result.Add((string)entry.Key, (string)entry.Value);
            }
            return result;
        }

        public bool StartAssignment(HostAssignmentContext context)
        {
            if (!_linuxConsumptionEnv.InStandbyMode)
            {
                return false;
            }

            if (_assignmentContext == null)
            {
                lock (_assignmentLock)
                {
                    if (_assignmentContext != null)
                    {
                        return _assignmentContext.Equals(context);
                    }
                    _assignmentContext = context;
                }

                // set a flag which will cause any incoming http requests to buffer
                // until specialization is complete
                // the host is guaranteed not to receive any requests until AFTER assign
                // has been initiated, so setting this flag here is sufficient to ensure
                // that any subsequent incoming requests while the assign is in progress
                // will be delayed until complete
                _linuxConsumptionEnv.DelayRequests();

                // start the specialization process in the background
                Task.Run(async () => await Assign(context));

                return true;
            }
            else
            {
                // No lock needed here since _assignmentContext is not null when we are here
                return _assignmentContext.Equals(context);
            }
        }

        private async Task Assign(HostAssignmentContext assignmentContext)
        {
            try
            {
                // first make all environment and file system changes required for
                // the host to be specialized
                assignmentContext.ApplyAppSettings();

                KuduEventGenerator.Log(null).LogMessage(EventLevel.Informational, assignmentContext.SiteName,
                    $"Mounting file share at {DateTime.UtcNow}", string.Empty);

                // Limit the amount of time time we allow for mounting to complete
                var mounted = await MountFileShareWithin(TimeSpan.FromSeconds(30));

                KuduEventGenerator.Log(null).LogMessage(EventLevel.Informational, assignmentContext.SiteName,
                    $"Mount file share result: {mounted} at {DateTime.UtcNow}", string.Empty);
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                // all assignment settings/files have been applied so we can flip
                // the switch now on specialization
                // even if there are failures applying context above, we want to
                // leave placeholder mode
                _linuxConsumptionEnv.FlagAsSpecializedAndReady();
                _linuxConsumptionEnv.ResumeRequests();
            }
        }

        private async Task<bool> MountFileShareWithin(TimeSpan timeLimit)
        {
            var startTime = DateTime.UtcNow;
            
            try
            {
                return await OperationManager.ExecuteWithTimeout(MountFileShare(), timeLimit);
            }
            catch (Exception e)
            {
                KuduEventGenerator.Log(null).LogMessage(EventLevel.Warning, ServerConfiguration.GetApplicationName(),
                    nameof(MountFileShareWithin), e.ToString());
                return false;
            }
            finally
            {
                KuduEventGenerator.Log(null).LogMessage(EventLevel.Informational,
                    ServerConfiguration.GetApplicationName(),
                    $"Time taken to mount = {(DateTime.UtcNow - startTime).TotalMilliseconds}", string.Empty);
            }
        }

        private async Task<bool> MountFileShare()
        {
            return await _meshPersistentFileSystem.MountFileShare();
        }
    }
}
