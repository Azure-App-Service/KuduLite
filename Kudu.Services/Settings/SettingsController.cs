using System;
using System.Net;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Settings;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Kudu.Core.K8SE;

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
        public IActionResult Set([FromBody]JObject newSettings)
        {
            if (newSettings == null)
            {
                return BadRequest();
            }

            // We support two formats here:
            // 1. For backward compat, we support {key: 'someKey', value: 'someValue' }
            // 2. The preferred format is { someKey = 'someValue' }
            // Note that #2 allows multiple settings to be set, e.g. { someKey = 'someValue', someKey2 = 'someValue2' }

            try
            {
                return _deploymentLock.LockOperation<IActionResult>(() =>
                {
                    JToken keyToken, valueToken;
                    if (newSettings.Count == 2 && newSettings.TryGetValue("key", out keyToken) && newSettings.TryGetValue("value", out valueToken))
                    {
                        string key = keyToken.Value<string>();

                        if (String.IsNullOrEmpty(key))
                        {
                            return BadRequest();
                        }

                        _settingsManager.SetValue(key, valueToken.Value<string>());
                    }
                    else
                    {
                        foreach (var keyValuePair in newSettings)
                        {
                            _settingsManager.SetValue(keyValuePair.Key, keyValuePair.Value.Value<string>());
                        }
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
            IDictionary<string, string> appSettings = new Dictionary<string, string>();
            if (K8SEDeploymentHelper.IsK8SEEnvironment() && HttpContext != null)
            {
                appSettings = (IDictionary<string, string>)HttpContext.Items["appSettings"];
            }

            return Ok(_settingsManager.GetValues(appSettings));
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

            string value = _settingsManager.GetValue(key);

            if (value == null)
            {
                return NotFound(String.Format(Resources.SettingDoesNotExist, key));
            }

            return Content(value);
        }
    }
}
