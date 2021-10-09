using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Text;

namespace Kudu.Core.Tracing
{
    class KuduEvent
    {
        public int level = (int)EventLevel.Informational;
        public string siteName = string.Empty;
        public string projectType = string.Empty;
        public string result = string.Empty;
        public string error = string.Empty;
        public long deploymentDurationInMilliseconds = 0;
        public string siteMode = string.Empty;
        public string scmType = string.Empty;
        public string vsProjectId = string.Empty;
        public string jobName = string.Empty;
        public string scriptExtension = string.Empty;
        public string jobType = string.Empty;
        public string trigger = string.Empty;
        public string method = string.Empty;
        public string path = string.Empty;
        public string Message = string.Empty;
        public string exception = string.Empty;
        public string route = string.Empty;
        public string userAgent = string.Empty;
        public string requestId = string.Empty;
        public string buildVersion = Constants.KuduBuild;
        public string address = string.Empty;
        public string verb = string.Empty;
        public int statusCode = 0;
        public long latencyInMilliseconds = 0;

        public override string ToString()
        {
            return $"{level},{siteName},{projectType},{result},{NormalizeString(error)},{deploymentDurationInMilliseconds},{siteMode},{scmType},{vsProjectId}," +
                $"{jobName},{scriptExtension},{jobType},{trigger},{method},{path},{NormalizeString(Message)},{NormalizeString(exception)}," +
                $"{route},{NormalizeString(userAgent)},{requestId},{buildVersion},{address},{verb},{statusCode},{latencyInMilliseconds}";
        }

        private string NormalizeString(string value)
        {
            // need to remove newlines for csv output
            value = value.Replace(System.Environment.NewLine, " ");
            value = value.Replace("\"", " ");

            // Wrap string literals in enclosing quotes
            // For string columns that may contain quotes and/or
            // our delimiter ',', before writing the value we
            // enclose in quotes. This allows us to define matching
            // groups based on quotes for these values.
            return $"\"{value}\"";
        }
    }
}
