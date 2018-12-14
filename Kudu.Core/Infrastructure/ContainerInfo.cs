using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Kudu.Core.Infrastructure
{

    public class SiteInstanceStats
    {
        [JsonProperty(PropertyName = "containers")]
        public Dictionary<string,ContainerInfo> appContainersOnThisInstance = new Dictionary<string, ContainerInfo>();
    }
    
    public class ContainerInfo
    {

        [JsonProperty(PropertyName = "read")] 
        public DateTime CurrentTimeStamp { get; set; }

        [JsonProperty(PropertyName = "preread")]
        public DateTime PreviousTimeStamp { get; set; }

        [JsonProperty(PropertyName = "cpu_stats")]
        public ContainerCpuStatistics CurrentCpuStats { get; set; }

        [JsonProperty(PropertyName = "precpu_stats")]
        public ContainerCpuStatistics PreviousCpuStats { get; set; }

        [JsonProperty(PropertyName = "memory_stats")]
        public ContainerMemoryStatistics MemoryStats { get; set; }

        [JsonProperty(PropertyName = "name")] public string Name { get; set; }

        [JsonProperty(PropertyName = "id")] public string Id { get; set; }

        [JsonProperty(PropertyName = "eth0")] public ContainerNetworkInterfaceStatistics Eth0 { get; set; }

        public string GetSiteName()
        {
            if (String.IsNullOrWhiteSpace(this.Name))
            {
                return "";
            }
            var startIndex = this.Name[0] == '/' ? 1 : 0;
            // Remove the last _0 or something from the container name
            var siteName = Regex.Replace(this.Name.Substring(startIndex), @"_\d+$", "");
            return siteName;
        }

    }

    public class ContainerCpuUsage
        {
            [JsonProperty(PropertyName = "total_usage")]
            public long TotalUsage { get; set; }

            [JsonProperty(PropertyName = "percpu_usage")]
            public List<long> PerCpuUsage { get; set; }

            [JsonProperty(PropertyName = "usage_in_kernelmode")]
            public long KernelModeUsage { get; set; }

            [JsonProperty(PropertyName = "usage_in_usermode")]
            public long UserModeUsage { get; set; }
        }

    public class ContainerThrottlingData
        {
            [JsonProperty(PropertyName = "periods")]
            public int Periods { get; set; }

            [JsonProperty(PropertyName = "throttled_periods")]
            public int ThrolltledPeriods { get; set; }

            [JsonProperty(PropertyName = "throttled_time")]
            public int ThrottledTime { get; set; }
        }

    public class ContainerCpuStatistics
        {
            [JsonProperty(PropertyName = "cpu_usage")]
            public ContainerCpuUsage CpuUsage { get; set; }

            [JsonProperty(PropertyName = "system_cpu_usage")]
            public long SystemCpuUsage { get; set; }

            [JsonProperty(PropertyName = "online_cpus")]
            public int OnlineCpuCount { get; set; }

            public ContainerThrottlingData ThrottlingData { get; set; }
        }

    public class ContainerMemoryStatistics
        {
            [JsonProperty(PropertyName = "usage")] public long Usage { get; set; }

            [JsonProperty(PropertyName = "max_usage")]
            public long MaxUsage { get; set; }

            [JsonProperty(PropertyName = "limit")] public long Limit { get; set; }
        }

    public class ContainerNetworkInterfaceStatistics
        {
            [JsonProperty(PropertyName = "rx_bytes")]
            public long RxBytes { get; set; }

            [JsonProperty(PropertyName = "rx_packets")]
            public long RxPackets { get; set; }

            [JsonProperty(PropertyName = "rx_errors")]
            public long RxErrors { get; set; }

            [JsonProperty(PropertyName = "rx_dropped")]
            public long RxDropped { get; set; }

            [JsonProperty(PropertyName = "tx_bytes")]
            public long TxBytes { get; set; }

            [JsonProperty(PropertyName = "tx_packets")]
            public long TxPackets { get; set; }

            [JsonProperty(PropertyName = "tx_errors")]
            public long TxErrors { get; set; }

            [JsonProperty(PropertyName = "tx_dropped")]
            public long TxDropped { get; set; }
        }


    public class ContainerSimpleStatus
        {
            [JsonProperty(PropertyName = "TimeStamp")]
            public DateTime TimeStamp { get; set; }

            [JsonProperty(PropertyName = "name")] public string Name { get; set; }

            [JsonProperty(PropertyName = "cpuUsage")]
            public long CpuUsage { get; set; }

            [JsonProperty(PropertyName = "systemCpuUsage")]
            public long SystemCpuUsage { get; set; }

            [JsonProperty(PropertyName = "memoryUsage")]
            public long MemoryUsage { get; set; }

            [JsonProperty(PropertyName = "maxMemoryUsage")]
            public long MaxMemoryUsage { get; set; }

            [JsonProperty(PropertyName = "memoryLimit")]
            public long MemoryLimit { get; set; }
        }

}