using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using Kudu.Core.Extensions;
using Microsoft.AspNetCore.Http;

namespace Kudu.Core.Settings
{
    public class EnvironmentSettingsProvider : BasicSettingsProvider
    {
        private static readonly Lazy<Dictionary<string, string>> _environmentSettingsFactory = new Lazy<Dictionary<string, string>>(GetEnvironmentSettingsInternal);
        private const string AppSettingPrefix = "APPSETTING_";

        public EnvironmentSettingsProvider(IDictionary<string, string> appSettings)
            :base(MergeSettings(appSettings), SettingsProvidersPriority.Environment)
        {   
        }

        public static IDictionary<string, string> MergeSettings(IDictionary<string, string> appSettings)
        {
            var envSetting = _environmentSettingsFactory.Value;
            if (appSettings != null)
            {
                foreach (var kv in appSettings)
                {
                    envSetting[kv.Key] = kv.Value;
                }
            }

            return envSetting;
        }

        private static Dictionary<string, string> GetEnvironmentSettingsInternal()
        {
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Add all the .net <appSettings> (which themselves include portal settings in Azure!).
            foreach (string name in ConfigurationManager.AppSettings)
            {
                settings[name] = ConfigurationManager.AppSettings[name];
            }

            // Go through all the environment variables and process those that are meant to be app settings.
            // In Azure, those will already have been present in ConfigurationManager.AppSettings when running
            // in the Kudu service. But when running in kudu.exe, they wouldn't (hence the need for this code)
            foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                var name = (string)entry.Key;

                if (name.StartsWith(AppSettingPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(AppSettingPrefix.Length);
                    settings[name] = (string)entry.Value;
                }
            }

            return settings;
        }
    }
}
