using System;
using System.Collections.Generic;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Settings;
using Kudu.Core.K8SE;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace Kudu.Services.Settings
{
    public class SettingsController : Controller
    {
        private const string DeploymentSettingsSection = "deployment";
        private readonly IDeploymentSettingsManager _settingsManager;
        private readonly IOperationLock _deploymentLock;

        public SettingsController(
            IDeploymentSettingsManager settingsManager,
            IDictionary<string, IOperationLock> namedLocks)
        {
            _settingsManager = settingsManager;
            _deploymentLock = namedLocks[Constants.DeploymentLockName];
        }

        /// <summary>
        /// Create or change some settings
        /// </summary>
        /// <param name="newSettings">The object containing the new settings</param>
        /// <returns></returns>
        public IActionResult Set([FromBody] JObject newSettings)
        {
            if (newSettings == null)
            {
                return BadRequest();
            }

            try
            {
                return _deploymentLock.LockOperation<IActionResult>(() =>
                {
                    foreach (var keyValuePair in newSettings)
                    {
                        _settingsManager.SetValue(keyValuePair.Key, keyValuePair.Value.Value<string>());
                    }

                    return NoContent();
                }, "Updating deployment setting", TimeSpan.FromSeconds(5));
            }
            catch (LockOperationException ex)
            {
                return StatusCode(StatusCodes.Status409Conflict, ex.Message);
            }
        }

        /// <summary>
        /// Delete a setting
        /// </summary>
        /// <param name="key">The key of the setting to delete</param>
        /// <returns></returns>
        public IActionResult Delete(string key)
        {
            if (String.IsNullOrEmpty(key))
            {
                return BadRequest();
            }

            try
            {
                return _deploymentLock.LockOperation(() =>
                {
                    _settingsManager.DeleteValue(key);

                    return NoContent();
                }, "Deleting deployment setting", TimeSpan.Zero);
            }
            catch (LockOperationException ex)
            {
                return StatusCode(StatusCodes.Status409Conflict, ex.Message);
            }
        }

        /// <summary>
        /// Get the list of all settings
        /// </summary>
        /// <returns></returns>
        public IActionResult GetAll()
        {
            return Ok(_settingsManager.GetValues(null));
        }

        /// <summary>
        /// Get the value of a setting
        /// </summary>
        /// <param name="key">The setting's key</param>
        /// <returns></returns>
        public IActionResult Get(string key)
        {
            if (String.IsNullOrEmpty(key))
            {
                return BadRequest();
            }

            var value = _settingsManager.GetValue(key);

            if (value == null)
            {
                return NotFound(String.Format(Resources.SettingDoesNotExist, key));
            }

            return Content(value);
        }
    }
}
