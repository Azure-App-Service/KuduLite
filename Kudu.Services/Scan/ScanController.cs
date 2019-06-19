using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Scan;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Commands;
using Kudu.Core.Tracing;
using Kudu.Services.Arm;
using Kudu.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Kudu.Services.Scan
{
    public class ScanController : Controller
    {
        private readonly ICommandExecutor _commandExecutor;
        private readonly ITracer _tracer;
        private readonly IOperationLock _scanLock;
        private readonly IScanManager _scanManager;
        private IEnvironment _webAppRuntimeEnvironment;
        String mainScanDirPath = null;

        public ScanController(ICommandExecutor commandExecutor, ITracer tracer, IDictionary<string, IOperationLock> namedLocks, IScanManager scanManager, IEnvironment webAppRuntimeEnvironment)
        {
            _commandExecutor = commandExecutor;
            _tracer = tracer;
            _scanLock = namedLocks["deployment"];
            _scanManager = scanManager;
            _webAppRuntimeEnvironment = webAppRuntimeEnvironment;
            mainScanDirPath = Path.Combine(_webAppRuntimeEnvironment.LogFilesPath, "kudu", "scan");
        }

        [HttpGet]
        public IActionResult ExecuteScan(string timeout)
        {
            IActionResult finalResult;
            if (timeout == null || timeout.Length == 0)
            {
                timeout = Constants.ScanTimeOut;
            }


            //Start sync scanning
            String timestamp = DateTime.UtcNow.ToString("yyy-MM-dd_HH-mm-ssZ");
            var result = _scanManager.StartScan(timeout, mainScanDirPath, timestamp);
            // Console.WriteLine("Running Asynchronously Track URL:"+ String.Format("/api/scan/track/{0}", timestamp));
            ScanUrl obj;

            //Check if files were modified after last scan
            if (result.IsCompleted && result.Result == ScanRequestResult.NoFileModifications)
            {
                //Create URL
                obj = new ScanUrl(
                Constants.NoModifiedFiles,
                 Constants.LastScanMsg, timestamp);
            }
            else
            {
                //Create URL
                obj = new ScanUrl(
                    UriHelper.GetRequestUri(Request).Authority + String.Format("/api/scan/{0}/track", timestamp),
                    getResultURL(timestamp), timestamp);
            }


            //result;
            return Ok(ArmUtils.AddEnvelopeOnArmRequest(obj, Request));

        }

        private string getResultURL(string timestamp)
        {
            return UriHelper.GetRequestUri(Request).Authority + String.Format("/api/scan/{0}/result", timestamp);
        }

        public IActionResult GetScanResults()
        {
            IActionResult result;

            using (_tracer.Step("ScanService.GetScanResults"))
            {
                List<ScanOverviewResult> results = _scanManager.GetResults(mainScanDirPath).ToList();
                foreach (ScanOverviewResult obj in results)
                {
                    obj.ScanResultsUrl = getResultURL(obj.Status.Id);
                }
                result = Ok(ArmUtils.AddEnvelopeOnArmRequest(results, Request));
            }

            return result;
        }

        [HttpGet]
        public async Task<IActionResult> GetScanStatus(String scanId)
        {
            using (_tracer.Step("ScanController.getScanStatus"))
            {
                var obj = await _scanManager.GetScanStatus(scanId, mainScanDirPath);
                if (obj == null)
                    return BadRequest();
                return Ok(obj);

            }
        }

        [HttpGet]
        public async Task<IActionResult> GetScanFile(String scanId)
        {
            using (_tracer.Step("ScanController.getScanStatus"))
            {
                var obj = await _scanManager.GetScanResultFile(scanId, mainScanDirPath);
                if (obj == null)
                    return BadRequest();
                return Ok(ArmUtils.AddEnvelopeOnArmRequest(obj, Request));

            }
        }


    }


}
