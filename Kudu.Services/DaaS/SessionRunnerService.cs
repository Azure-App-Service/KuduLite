
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Tracing;
using Kudu.Services.Performance;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Kudu.Services.Performance
{

    class DotNetMonitorMemoryDumpResponse
    {
        public string Path { get; set; }
    }

    /// <summary>
    /// ASP.NET core background HostedService that manages sessions
    /// submitted for DAAS for Linux apps
    /// </summary>
    public class SessionRunnerService : BackgroundService
    {
        private const string EgressProviderName = "monitorFile";

        private readonly ITracer _tracer;
        private readonly ITraceFactory _traceFactory;
        private readonly ISessionManager _sessionManager;
        private Dictionary<string, TaskAndCancellationToken> _runningSessions = new Dictionary<string, TaskAndCancellationToken>();
        private readonly HttpClient _dotnetMonitorClient;

        private static AllSafeLinuxLock _sessionLockFile;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sessionManager"></param>
        public SessionRunnerService(ITraceFactory traceFactory,
            ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
            _dotnetMonitorClient = CreateDotNetMonitorClient();
            _traceFactory = traceFactory;
            _tracer = _traceFactory.GetTracer();
        }

        private HttpClient CreateDotNetMonitorClient()
        {
            var webRequestHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
                {
                    return true;
                }
            };

            return new HttpClient(webRequestHandler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
        }

        /// <summary>
        /// Implement abstract ExecuteAsync method for BackgroundService
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (DotNetHelper.IsDotNetMonitorEnabled())
                {
                    await SessionRunner(stoppingToken);
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }

        private async Task SessionRunner(CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            await RunActiveSession();

            CleanupCompletedSessions();
        }

        private void CleanupCompletedSessions()
        {
            foreach (var sessionId in _runningSessions.Keys.ToList())
            {
                if (_runningSessions[sessionId].UnderlyingTask != null)
                {
                    var status = _runningSessions[sessionId].UnderlyingTask.Status;
                    if (status == TaskStatus.Canceled || status == TaskStatus.Faulted || status == TaskStatus.RanToCompletion)
                    {

                        TraceExtensions.Trace(_tracer, $"Task for Session {sessionId} has completed with status {status} on {System.Environment.MachineName}", sessionId.ToString());
                        _runningSessions.Remove(sessionId);
                    }
                }
            }
        }

        private async Task RunActiveSession()
        {
            Session activeSession = await _sessionManager.GetActiveSession();
            if (activeSession == null)
            {
                return;
            }

            if (activeSession.Instances.Any(x => x.Equals(GetInstanceId(), StringComparison.OrdinalIgnoreCase)))
            {
                if (_runningSessions.ContainsKey(activeSession.SessionId))
                {
                    // Data Collection for this session is in progress
                    return;
                }

                if (HasThisInstanceCollectedLogs(activeSession))
                {
                    // This instance has already collected logs for this session
                    return;
                }

                    CancellationTokenSource cts = new CancellationTokenSource();
                var sessionTask = RunToolForSessionAsync(activeSession, cts.Token);

                TaskAndCancellationToken t = new TaskAndCancellationToken
                {
                    UnderlyingTask = sessionTask,
                    CancellationSource = cts
                };

                _runningSessions[activeSession.SessionId] = t;
            }
        }

        private bool HasThisInstanceCollectedLogs(Session activeSession)
        {
            return activeSession.Logs != null 
                && activeSession.Logs.Any(x => x.Instance.Equals(GetInstanceId(), 
                StringComparison.OrdinalIgnoreCase));
        }

        private async Task RunToolForSessionAsync(Session activeSession, CancellationToken token)
        {
            var dotnetMonitorAddress = DotNetHelper.GetDotNetMonitorAddress();
            if (!string.IsNullOrWhiteSpace(dotnetMonitorAddress))
            {
                if (activeSession.Tool == DiagnosticTool.MemoryDump)
                {
                    string type = "Mini";
                    var resp =  await _dotnetMonitorClient.GetAsync($"{dotnetMonitorAddress}/dump/13640?egressProvider={EgressProviderName}&type={type}");
                    if (resp.IsSuccessStatusCode)
                    {
                        string responseBody = await resp.Content.ReadAsStringAsync();
                        var dotnetMonitorResponse = JsonConvert.DeserializeObject<DotNetMonitorMemoryDumpResponse>(responseBody);
                        await AddLogToActiveSession(dotnetMonitorResponse.Path);
                    }
                    
                    await MarkSessionAsComplete(activeSession, resp);
                }
            }
        }

        private async Task AddLogToActiveSession(string path)
        {
            var logFile = new LogFile()
            {
                Instance = System.Environment.MachineName,
                Name = Path.GetFileName(path),
                FullPath = path,
                Size = GetFileSize(path),
                RelativePath = ""
            };

            _sessionLockFile = new AllSafeLinuxLock(path, _traceFactory);
            if (_sessionLockFile.Lock("AcquireSessionLock"))
            {
                var activeSession = await _sessionManager.GetActiveSession();
                if (activeSession.Logs == null)
                {
                    activeSession.Logs = new List<LogFile>();
                }

                activeSession.Logs.Add(logFile);
                await _sessionManager.UpdateActiveSession(activeSession);
            }

            if (_sessionLockFile != null)
            {
                _sessionLockFile.Release();
            }
        }

        private long GetFileSize(string path)
        {
            return new System.IO.FileInfo(path).Length;
        }

        private async Task MarkSessionAsComplete(Session activeSession, HttpResponseMessage resp)
        {
        }

        private string GetInstanceId()
        {
            return System.Environment.MachineName;
        }

    }
}
