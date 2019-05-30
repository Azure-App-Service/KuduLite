using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Scan;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core.Commands;
using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Core.Scan
{
    public class ScanManager : IScanManager
    {

        private readonly ICommandExecutor _commandExecutor;
        private readonly ITracer _tracer;
        private readonly IOperationLock _scanLock;
        private ExternalCommandFactory _externalCommandFactory = null;
        private IEnvironment _environment;

        public ScanManager(ICommandExecutor commandExecutor, ITracer tracer, IDictionary<string, IOperationLock> namedLocks, IEnvironment environment, IDeploymentSettingsManager settings)
        {
            var repositoryPath = environment.RootPath;
            _commandExecutor = commandExecutor;
            _tracer = tracer;
            _scanLock = namedLocks["deployment"];
            _externalCommandFactory = new ExternalCommandFactory(environment, settings, repositoryPath); 
            _environment = environment;
        }

        private static void UpdateScanStatus(String folderPath,ScanStatus status)
        {
            String filePath = Path.Combine(folderPath, Constants.ScanStatusFile);
            JObject obj = ReadScanStatusFile("", "", Constants.ScanStatusFile, folderPath);
            ScanStatusResult scr = new ScanStatusResult();

            //Create new Scan Id if file is empty
            //else get existing scan Id
            if(obj == null || obj["id"] == null)
            {
                scr.Id = DateTime.UtcNow.ToString("yyy-MM-dd_HH-mm-ssZ");
            }
            else
                scr.Id = (String)obj["id"];

            //Update status of the scan
            scr.Status = status;
            File.WriteAllText(filePath, JsonConvert.SerializeObject(scr));

        }

        public async Task<ScanRequestResult> StartScan(String timeout,String mainScanDirPath,String timestamp)
        {
            using (_tracer.Step("Start scan in the background"))
            {
                String folderPath = Path.Combine(mainScanDirPath, Constants.ScanFolderName + timestamp);
                String filePath = Path.Combine(folderPath, Constants.ScanStatusFile);
                ScanStatusResult scr = null;

                //Create unique scan folder and scan status file
                _scanLock.LockOperation(() =>
                {
                    //Create uniue scan directory for current scan
                    FileSystemHelpers.CreateDirectory(folderPath);
                    Console.WriteLine("Unique scan directory created");

                    //Create scan status file inside folder
                    FileSystemHelpers.CreateFile(filePath).Close();

                    UpdateScanStatus(folderPath, ScanStatus.Starting);

                }, "Creating unique scan folder", TimeSpan.Zero);

                //Start Backgorund Scan
                using (var timeoutCancellationTokenSource = new CancellationTokenSource())
                {
                    var successfullyScanned = PerformBackgroundScan(_tracer, _scanLock, folderPath, _externalCommandFactory,_environment,timeoutCancellationTokenSource.Token);

                    //Wait till scan task completes or the timeout goes off
                    if (await Task.WhenAny(successfullyScanned, Task.Delay(Int32.Parse(timeout), timeoutCancellationTokenSource.Token)) == successfullyScanned)
                    {
                        //If scan task completes before timeout
                        //Delete excess scan folders, just keep the maximum number allowed
                        await deletePastScans(mainScanDirPath);

                        return successfullyScanned.Result
                        ? ScanRequestResult.RunningAynschronously
                        : ScanRequestResult.AsyncScanFailed;
                    }
                    else
                    {
                        //Timeout went off before scan task completion
                        //Cancel scan task
                        timeoutCancellationTokenSource.Cancel();

                        //Scan process will be cancelled
                        //wait till scan status file is appropriately updated
                        await successfullyScanned;
                       
                        //Delete excess scan folders, just keep the maximum number allowed
                        await deletePastScans(mainScanDirPath);

                        return ScanRequestResult.AsyncScanFailed;
                        
                    }
                }

                
                

                /*return successfullyScanned
                    ? ScanRequestResult.RunningAynschronously
                    : ScanRequestResult.AsyncScanFailed;*/
            }

            /*using (_tracer.Step("Start scan in the background"))
            {
            String folderPath = Path.Combine(mainScanDirPath, Constants.ScanFolderName + timestamp);

                //Create unique scan folder
                _scanLock.LockOperation(() =>
                {
                    FileSystemHelpers.CreateDirectory(folderPath);
                    Console.WriteLine("Unique scan directory created");
                },"Creating unique scan folder",TimeSpan.Zero);

            var successfullyScanned = false;
            //Start Backgorund Scan
            successfullyScanned = await PerformBackgroundScan(_tracer, requestUri, _scanLock, _commandExecutor, folderPath);

            //Delete all scan folders, just keep the maximum number allowed
            await deletePastScans(mainScanDirPath);

            return successfullyScanned
                ? ScanRequestResult.RunningAynschronously
                : ScanRequestResult.AsyncScanFailed;
        }*/

        }

        public static async Task deletePastScans(String mainDirectory)
        {
            //Run task to delete unwanted previous scans
            await Task.Run(() =>
            {
                //Main scan directory where all scans are stored
                DirectoryInfo info = new DirectoryInfo(mainDirectory);
                //Get sub directories and sort them by time of creation
                DirectoryInfo[] subDirs = info.GetDirectories().OrderByDescending(p => p.CreationTime).ToArray();
                //Get max number of scans to store as history
                int maxCnt = Int32.Parse(Constants.MaxScans);


                if (subDirs.Length > maxCnt)
                {
                    int diff = subDirs.Length - maxCnt;
                    for (int i = subDirs.Length - 1; diff > 0; diff--, i--)
                    {
                        //Delete oldest directories till we only have max number and no more than that
                        Console.WriteLine("Delete Folder:" + subDirs[i].FullName);
                        subDirs[i].Delete(true);
                    }
                }
            });

        }

        public async Task<JObject> GetScanStatus(String scanId,String mainScanDirPath)
        {
            JObject obj = null;
            await Task.Run(() =>
            {
                obj = ReadScanStatusFile(scanId, mainScanDirPath, Constants.ScanStatusFile,null);
            });

                return obj;
        }

        public async Task<String> GetScanResultFile(String scanId,String mainScanDirPath)
        {
            //JObject statusRes = await GetScanStatus(scanId, mainScanDirPath);
            String text = null;
            //Run task to read the result file
            await Task.Run(() =>
            {
                String path = Path.Combine(mainScanDirPath, Constants.ScanFolderName + scanId, Constants.ScanLogFile);
                text = FileSystemHelpers.ReadAllTextFromFile(path);
            });

            //All the contents of the file in plain text
                return text;
        }

        private static JObject ReadScanStatusFile(String scanId, String mainScanDirPath, String fileName,String folderName)
        {
            JObject obj = null;
            String readPath = Path.Combine(mainScanDirPath, Constants.ScanFolderName + scanId, fileName);

            //Give preference to folderName if given
            if(folderName != null)
            {
                readPath = Path.Combine(folderName, fileName);
            }

            //Read json file and deserialize into JObject
            using (FileStream file = System.IO.File.OpenRead(readPath))
            {
                using (StreamReader sr = new StreamReader(file))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    obj = (JObject)serializer.Deserialize(sr, typeof(JObject));
                }

            }
            return obj;
        }

      //  private static 
        public static async Task<bool> PerformBackgroundScan(ITracer _tracer, IOperationLock _scanLock, String folderPath,ExternalCommandFactory _externalCommandFactory,IEnvironment _environment,CancellationToken token)
        {

            var successfulScan = true;

            await Task.Run(() =>
            {
               
                    _scanLock.LockOperation(() =>
                    {
                        
                        String filePath = Path.Combine(folderPath,Constants.ScanStatusFile);

                        Console.WriteLine("Starting Command Executor");
                        String logFilePath = Path.Combine(folderPath, Constants.ScanLogFile);

                        UpdateScanStatus(folderPath, ScanStatus.Executing);

                        // Create executable process and run script
                        Executable exe = _externalCommandFactory.BuildExternalCommandExecutable("", _environment.WebRootPath, NullLogger.Instance);
                        Process _executingProcess = exe.CreateProcess(Constants.ScanCommand + " " + logFilePath);

                        _executingProcess.Start();
                        _executingProcess.BeginErrorReadLine();
                        _executingProcess.BeginOutputReadLine();

                        //Check if process is completing before timeout
                        while (!_executingProcess.HasExited)
                        {
                            //Process still running, but timeout is done
                            if (token.IsCancellationRequested)
                            {

                                //Kill process
                                _executingProcess.CancelErrorRead();
                                _executingProcess.CancelOutputRead();
                                _executingProcess.Kill(includesChildren: true,_tracer);

                                //Wait for process to be completely killed
                                _executingProcess.WaitForExit();
                                successfulScan = false;
                                Console.WriteLine("Token Cancellation requested! Terminating process! Time: "+ DateTime.UtcNow.ToString("yyy-MM-dd_HH-mm-ssZ"));

                                //Update status file
                                UpdateScanStatus(folderPath, ScanStatus.Failed);
                                break;
                            }
                        }

                        
                        Console.WriteLine("Done with Command Executor");

                        //Update status file with success
                        if (successfulScan)
                        {
                            UpdateScanStatus(folderPath, ScanStatus.Success);
                        }
                       

                    }, "Performing continuous scan", TimeSpan.Zero);
               
                
              
            });

            return successfulScan;
            /*var deploymentTask = Task.Run(() =>
            {
                using (_tracer.Step("ScanService.ExecuteScan"))
                {
                    CommandResult result = null;
                    try
                    {
                            _scanLock.LockOperationAsync(() =>
                        {
                            result = _commandExecutor.ExecuteCommand("C:\\Users\\t-puvasu\\Desktop\\Scripts\\scan.bat", "");
                            // return Ok(result);
                        }, "Performing scan", TimeSpan.MaxValue);
                            //  return Ok();
                    }
                    catch(LockOperationException e)
                    {

                    }
                }
            });*/
        }
    }
}
