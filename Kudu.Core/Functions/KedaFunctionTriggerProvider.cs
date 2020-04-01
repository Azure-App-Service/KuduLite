using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Kudu.Core.Functions
{
    /// <summary>
    /// Returns "<see cref="IEnumerable<ScaleTrigger>"/> for KEDA scalers" 
    /// </summary>
    public class KedaFunctionTriggerProvider
    {
        public IEnumerable<ScaleTrigger> GetFunctionTriggers(string zipFilePath)
        {
            if (!File.Exists(zipFilePath))
            {
                return null;
            }

            List<ScaleTrigger> kedaScaleTriggers = new List<ScaleTrigger>();
            using (var zip = ZipFile.OpenRead(zipFilePath))
            {
                var entries = zip.Entries
                    .Where(e => IsFunctionJson(e.FullName));

                foreach (var entry in entries)
                {
                    using (var stream = entry.Open())
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            var functionTriggers = ParseFunctionJson(GetFunctionName(entry), reader.ReadToEnd());
                            if (functionTriggers?.Any() == true)
                            {
                                kedaScaleTriggers.AddRange(functionTriggers);
                            }
                        }
                    }
                }
            }

            bool IsFunctionJson(string fullName)
            {
                return fullName.EndsWith(Constants.FunctionsConfigFile) &&
                       fullName.Count(c => c == '/' || c == '\\') == 1;
            }
            
            return kedaScaleTriggers;
        }

        public IEnumerable<ScaleTrigger> ParseFunctionJson(string functionName, string functionJson)
        {
            var json = JObject.Parse(functionJson);
            if (json.TryGetValue("disabled", out JToken value))
            {
                string stringValue = value.ToString();
                if (!bool.TryParse(stringValue, out bool disabled))
                {
                    string expandValue = System.Environment.GetEnvironmentVariable(stringValue);
                    disabled = string.Equals(expandValue, "1", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(expandValue, "true", StringComparison.OrdinalIgnoreCase);
                }

                if (disabled)
                {
                    return null;
                }
            }

            var excluded = json.TryGetValue("excluded", out value) && (bool)value;
            if (excluded)
            {
                return null;
            }

            var triggers = new List<ScaleTrigger>();
            foreach (JObject binding in (JArray)json["bindings"])
            {
                var type = (string)binding["type"];
                if (type.EndsWith("Trigger", StringComparison.OrdinalIgnoreCase))
                {
                    var scaleTrigger = new ScaleTrigger
                    {
                        Type = type,
                        Metadata = new Dictionary<string, string>()
                    };
                    foreach (var property in binding)
                    {
                        if (property.Value.Type == JTokenType.String)
                        {
                            scaleTrigger.Metadata.Add(property.Key, property.Value.ToString());
                        }
                    }

                    scaleTrigger.Metadata.Add("functionName", functionName);
                    triggers.Add(scaleTrigger);
                }
            }

            return triggers;
        }

        private static string GetFunctionName(ZipArchiveEntry zipEnetry)
        {
            if (string.IsNullOrWhiteSpace(zipEnetry?.FullName))
            {
                return string.Empty;
            }

            return zipEnetry.FullName.Split('/').Length == 2 ? zipEnetry.FullName.Split('/')[0] : zipEnetry.FullName.Split('\\')[0];
        }
    }
}
