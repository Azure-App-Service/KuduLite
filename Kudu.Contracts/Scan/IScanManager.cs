using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Kudu.Contracts.Scan
{
    public interface IScanManager
    {
        Task<ScanRequestResult> StartScan(
            String timeout,
            String mainScanDirPath,
            String timestamp);

        Task<JObject> GetScanStatus(
            String scanId,
            String mainScanDirPath);

        Task<String> GetScanResultFile(
            String scanId,
            String mainScanDirPath);
    }
}

