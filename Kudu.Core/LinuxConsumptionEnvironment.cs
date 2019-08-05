using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kudu.Contracts;
using Kudu.Contracts.Settings;

namespace Kudu.Core
{
    public class LinuxConsumptionEnvironment : ILinuxConsumptionEnvironment
    {
        private readonly ReaderWriterLockSlim _delayLock = new ReaderWriterLockSlim();
        private TaskCompletionSource<object> _delayTaskCompletionSource;
        private bool? _standbyMode;

        public LinuxConsumptionEnvironment()
        {
            _delayTaskCompletionSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            _delayTaskCompletionSource.SetResult(null);
        }

        public bool DelayRequestsEnabled => !DelayCompletionTask.IsCompleted;

        public Task DelayCompletionTask
        {
            get
            {
                _delayLock.EnterReadLock();
                try
                {
                    return _delayTaskCompletionSource.Task;
                }
                finally
                {
                    _delayLock.ExitReadLock();
                }
            }
        }

        public bool InStandbyMode
        {
            get
            {
                // once set, never reset
                if (_standbyMode != null)
                {
                    return _standbyMode.Value;
                }
                if (IsPlaceHolderModeEnabled())
                {
                    return true;
                }

                // no longer standby mode
                _standbyMode = false;

                return _standbyMode.Value;
            }
        }

        Task ILinuxConsumptionEnvironment.DelayCompletionTask => throw new NotImplementedException();

        public void DelayRequests()
        {
            _delayLock.EnterUpgradeableReadLock();
            try
            {
                if (_delayTaskCompletionSource.Task.IsCompleted)
                {
                    _delayLock.EnterWriteLock();
                    try
                    {
                        _delayTaskCompletionSource = new TaskCompletionSource<object>();
                    }
                    finally
                    {
                        _delayLock.ExitWriteLock();
                    }
                }
            }
            finally
            {
                _delayLock.ExitUpgradeableReadLock();
            }
        }

        public void ResumeRequests()
        {
            _delayLock.EnterReadLock();
            try
            {
                _delayTaskCompletionSource?.SetResult(null);
            }
            finally
            {
                _delayLock.ExitReadLock();
            }
        }

        public void FlagAsSpecializedAndReady()
        {
            System.Environment.SetEnvironmentVariable(SettingsKeys.PlaceholderMode, "0");
            System.Environment.SetEnvironmentVariable(SettingsKeys.ContainerReady, "1");
        }

        private bool IsPlaceHolderModeEnabled()
        {
            // If WEBSITE_PLACEHOLDER_MODE is not set, we consider the container as a placeholder
            string placeHolderMode = System.Environment.GetEnvironmentVariable(SettingsKeys.PlaceholderMode);
            return placeHolderMode == "1" || string.IsNullOrEmpty(placeHolderMode);
        }
    }
}
