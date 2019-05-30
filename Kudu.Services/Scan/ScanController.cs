using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Scan;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Commands;
using Kudu.Core.Tracing;
using Kudu.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

        public ScanController(ICommandExecutor commandExecutor, ITracer tracer, IDictionary<string, IOperationLock> namedLocks,IScanManager scanManager, IEnvironment webAppRuntimeEnvironment)
        {
            _commandExecutor = commandExecutor;
            _tracer = tracer;
            _scanLock = namedLocks["scan"];
            _scanManager = scanManager;
            _webAppRuntimeEnvironment = webAppRuntimeEnvironment;
            mainScanDirPath = Path.Combine(_webAppRuntimeEnvironment.LogFilesPath, "kudu", "scan");
        }

        [HttpGet]
        public IActionResult ExecuteScan(string timeout)
        {
            Boolean isAsync = true;
           // String filePath = Path.Combine(_webAppRuntimeEnvironment.LogFilesPath, "kudu", "scan");
            // var result = await _scanManager.StartScan(isAsync, UriHelper.GetRequestUri(Request));

            if (isAsync)
            {
                //Start sync scanning
                String timestamp = DateTime.UtcNow.ToString("yyy-MM-dd_HH-mm-ssZ");
                var result = _scanManager.StartScan(timeout, mainScanDirPath,timestamp);
               // Console.WriteLine("Running Asynchronously Track URL:"+ String.Format("/api/scan/track/{0}", timestamp));

                //Create URL
                JObject obj = new JObject(
                    new JProperty("TrackingURL", UriHelper.GetRequestUri(Request).Authority + String.Format("/api/scan/{0}/track", timestamp)),
                    new JProperty("ResultURL", UriHelper.GetRequestUri(Request).Authority + String.Format("/api/scan/{0}/result", timestamp)));
               /* Response.GetTypedHeaders().Location =
                            new Uri(UriHelper.GetRequestUri(Request),
                                String.Format("/api/scan/track/{0}",timestamp));*/

                //result;
                return Accepted(obj);
            }

            return Ok();

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
                /*//  String filePath = Path.Combine(_webAppRuntimeEnvironment.LogFilesPath, "kudu", "scan", "status.json");
                String scanStatusPath = Path.Combine(mainScanDirPath, Constants.ScanFolderName + scanId,Constants.ScanStatusFile);

                using (FileStream file = System.IO.File.OpenRead(scanStatusPath))
                {
                    using(StreamReader sr = new StreamReader(file))
                    {
                        JsonSerializer serializer = new JsonSerializer();
                        JObject obj = (JObject)serializer.Deserialize(sr, typeof(JObject));
                        return Ok(obj);
                    }
                    
                }*/
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
                return Ok(obj);
                /*//  String filePath = Path.Combine(_webAppRuntimeEnvironment.LogFilesPath, "kudu", "scan", "status.json");
                String scanStatusPath = Path.Combine(mainScanDirPath, Constants.ScanFolderName + scanId,Constants.ScanStatusFile);

                using (FileStream file = System.IO.File.OpenRead(scanStatusPath))
                {
                    using(StreamReader sr = new StreamReader(file))
                    {
                        JsonSerializer serializer = new JsonSerializer();
                        JObject obj = (JObject)serializer.Deserialize(sr, typeof(JObject));
                        return Ok(obj);
                    }
                    
                }*/
            }
        }

    }


}
