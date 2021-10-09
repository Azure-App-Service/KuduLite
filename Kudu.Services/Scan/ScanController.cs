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

        private readonly ITracer _tracer;
        private readonly IScanManager _scanManager;
        private IEnvironment _webAppRuntimeEnvironment;
        string mainScanDirPath = null;

        public ScanController(ITracer tracer, IScanManager scanManager, IEnvironment webAppRuntimeEnvironment)
        {
            _tracer = tracer;
            _scanManager = scanManager;
            _webAppRuntimeEnvironment = webAppRuntimeEnvironment;
            mainScanDirPath = Path.Combine(_webAppRuntimeEnvironment.LogFilesPath, "kudu", "scan");
        }

        [HttpGet]
        public IActionResult ExecuteScan([FromQuery] string timeout,[FromQuery] Boolean checkModified = true)
        {

            if (timeout == null || timeout.Length == 0)
            {
                timeout = Constants.ScanTimeOutMillSec;
            }

            if(checkModified == null)
            {
                checkModified = true;
            }

            //Start async scanning
            String id = DateTime.UtcNow.ToString("yyy-MM-dd_HH-mm-ssZ");
            var result = _scanManager.StartScan(timeout, mainScanDirPath, id, Request.Headers["Host"],checkModified);
            ScanUrl obj;

            //Check if files were modified after last scan
            if (result.IsCompleted && result.Result == ScanRequestResult.NoFileModifications)
            {
                //Create URL
                obj = new ScanUrl(null, null, id, Resources.NoScanModifiedFiles+" "+ Resources.LastScanMsg);
            }
            else if(result.IsCompleted && result.Result == ScanRequestResult.ScanAlreadyInProgress)
            {
                //Create URL
                obj = new ScanUrl(null, null, id, Resources.ScanInProgress);
            }
            else
            {
                //Create URL
                obj = new ScanUrl(_webAppRuntimeEnvironment.AppBaseUrlPrefix + String.Format("/api/scan/{0}/track", id), getResultURL(id), id,"");
            }


            //result;
            return Ok(ArmUtils.AddEnvelopeOnArmRequest(obj, Request));

        }

        private string getResultURL(string id)
        {
            return _webAppRuntimeEnvironment.AppBaseUrlPrefix + String.Format("/api/scan/{0}/result", id);
        }

        [HttpGet]
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
                {
                    return BadRequest();
                }
                return Ok(obj);

            }
        }

        [HttpGet]
        public async Task<IActionResult> GetScanLog(String scanId)
        {
            using (_tracer.Step("ScanController.getScanStatus"))
            {
                var obj = await _scanManager.GetScanResultFile(scanId, mainScanDirPath);
                if (obj == null)
                {
                    return BadRequest();
                }
                return Ok(ArmUtils.AddEnvelopeOnArmRequest(obj, Request));

            }
        }

        [HttpDelete]
        public void StopScan()
        {
            _scanManager.StopScan(mainScanDirPath);
        }
        
    }


}
