
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Services.Performance;
using Microsoft.Extensions.Hosting;

namespace Kudu.Services.DaaS
{

    /// <summary>
    /// ASP.NET core background HostedService that manages sessions
    /// submitted for DAAS for Linux apps
    /// </summary>
    public class SessionRunnerService : BackgroundService
    {
        private const double MaxAllowedSessionTimeInMinutes = 15;

        private readonly ISessionManager _sessionManager;
        private readonly ConcurrentDictionary<string, TaskAndCancellationToken> _runningSessions = new ConcurrentDictionary<string, TaskAndCancellationToken>();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sessionManager"></param>
        public SessionRunnerService(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
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
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        private async Task SessionRunner(CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            await RunActiveSession(stoppingToken);
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
                        DaasLogger.LogSessionMessage($"Task for Session completed with status {status}", sessionId);
                        _runningSessions.TryRemove(sessionId, out _);
                    }
                }
            }
        }

        private async Task RunActiveSession(CancellationToken stoppingToken)
        {
            Session activeSession = await _sessionManager.GetActiveSessionAsync();
            if (activeSession == null)
            {
                return;
            }

            // Check if all instances are finished with log collection
            if (await _sessionManager.CheckandCompleteSessionIfNeededAsync(activeSession))
            {
                return;
            }

            if (DateTime.UtcNow.Subtract(activeSession.StartTime).TotalMinutes > MaxAllowedSessionTimeInMinutes)
            {
                await _sessionManager.CheckandCompleteSessionIfNeededAsync(activeSession, forceCompletion: true);
            }

            if (_sessionManager.ShouldCollectOnCurrentInstance(activeSession))
            {
                if (_runningSessions.ContainsKey(activeSession.SessionId))
                {
                    // data Collection for this session is in progress
                    return;
                }

                if (_sessionManager.HasThisInstanceCollectedLogs(activeSession))
                {
                    // This instance has already collected logs for this session
                    return;
                }

                var sessionTask = _sessionManager.RunToolForSessionAsync(activeSession, stoppingToken);

                TaskAndCancellationToken t = new TaskAndCancellationToken
                {
                    UnderlyingTask = sessionTask,
                    CancellationToken = stoppingToken
                };

                _runningSessions[activeSession.SessionId] = t;
            }
        }
    }
}
