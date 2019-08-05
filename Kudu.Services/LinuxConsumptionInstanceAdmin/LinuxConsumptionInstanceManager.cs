using Kudu.Contracts;
using Kudu.Services.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Kudu.Core.Infrastructure;

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
        private readonly HttpClient _client;

        /// <summary>
        /// Create a manager to specialize KuduLite when it is running in Service Fabric Mesh
        /// </summary>
        /// <param name="linuxConsumptionEnv">Environment variables</param>
        public LinuxConsumptionInstanceManager(ILinuxConsumptionEnvironment linuxConsumptionEnv)
        {
            _linuxConsumptionEnv = linuxConsumptionEnv;
            _client = new HttpClient();
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

        public async Task<string> ValidateContext(HostAssignmentContext assignmentContext)
        {
            string error = null;
            HttpResponseMessage response = null;
            try
            {
                var zipUrl = assignmentContext.ZipUrl;
                if (!string.IsNullOrEmpty(zipUrl))
                {
                    // make sure the zip uri is valid and accessible
                    await OperationManager.AttemptAsync(async () =>
                    {
                        try
                        {
                            var request = new HttpRequestMessage(HttpMethod.Head, zipUrl);
                            response = await _client.SendAsync(request);
                            response.EnsureSuccessStatusCode();
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                    }, retries: 2, delayBeforeRetry: 300 /*ms*/);
                }
            }
            catch (Exception)
            {
                error = $"Invalid zip url specified (StatusCode: {response?.StatusCode})";
            }

            return error;
        }

        private async Task Assign(HostAssignmentContext assignmentContext)
        {
            try
            {
                // first make all environment and file system changes required for
                // the host to be specialized
                assignmentContext.ApplyAppSettings();
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
    }
}
