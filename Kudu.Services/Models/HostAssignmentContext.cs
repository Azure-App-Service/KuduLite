using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Kudu.Contracts.Settings;
using Kudu.Core;

namespace Kudu.Services.Models
{
    public class HostAssignmentContext
    {
        [JsonProperty("siteId")]
        public int SiteId { get; set; }

        [JsonProperty("siteName")]
        public string SiteName { get; set; }

        [JsonProperty("environment")]
        public Dictionary<string, string> Environment { get; set; }

        [JsonProperty("lastModifiedTime")]
        public DateTime LastModifiedTime { get; set; }

        public string ZipUrl
        {
            get
            {
                if (Environment.ContainsKey(SettingsKeys.RunFromZip))
                {
                    return Environment[SettingsKeys.RunFromZip];
                }
                else
                {
                    return string.Empty;
                }
            }
        }

        public bool Equals(HostAssignmentContext other)
        {
            if (other == null)
            {
                return false;
            }

            return SiteId == other.SiteId && LastModifiedTime.CompareTo(other.LastModifiedTime) == 0;
        }

        public void ApplyAppSettings()
        {
            foreach (var pair in Environment)
            {
                System.Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
