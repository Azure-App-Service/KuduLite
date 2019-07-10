using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Scan;
using Kudu.Contracts.Settings;
using Kudu.Contracts.Tracing;
using Kudu.Core.Commands;
using Kudu.Core.Deployment.Generator;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Kudu.Core.Scan
{
    public class ScanManager : IScanManager
    {

        private readonly ICommandExecutor _commandExecutor;
        private readonly ITracer _tracer;
        private readonly IOperationLock _scanLock;
        private static readonly string DATE_TIME_FORMAT = "yyyy-MM-dd_HH-mm-ssZ";
       // private string tempScanFilePath = null;

        public ScanManager(ICommandExecutor commandExecutor, ITracer tracer, IDictionary<string, IOperationLock> namedLocks)
        {
            _commandExecutor = commandExecutor;
            _tracer = tracer;
            _scanLock = namedLocks["deployment"];
        }

        private static void UpdateScanStatus(String folderPath,ScanStatus status)
        {
            String filePath = Path.Combine(folderPath, Constants.ScanStatusFile);
            ScanStatusResult obj = ReadScanStatusFile("", "", Constants.ScanStatusFile, folderPath);

            //Create new Scan Id if file is empty
            //else get existing scan Id
            if(obj == null || obj.Id == null)
            {
                obj = new ScanStatusResult();
                obj.Id = DateTime.UtcNow.ToString(DATE_TIME_FORMAT);
            }
            
            //Update status of the scan
            obj.Status = status;
            File.WriteAllText(filePath, JsonConvert.SerializeObject(obj));

        }

        private Boolean CheckModifications(String mainScanDirPath)
        {
            //Create path of manifest file
            string manifestFilePath = Path.Combine(mainScanDirPath, Constants.ScanManifest);

            //Check if manifest file exists
            //This means atleast 1 previous scan has been done
            if (FileSystemHelpers.FileExists(manifestFilePath))
            {
                using (FileStream file = System.IO.File.OpenRead(manifestFilePath))
                {
                    using (StreamReader sr = new StreamReader(file))
                    {
                        JsonSerializer serializer = new JsonSerializer();

                        //Read the manifest file into JSON object
                        JObject obj = (JObject)serializer.Deserialize(sr, typeof(JObject));

                        //Check for modifications
                        return IsFolderModified(obj, Constants.ScanDir);
                    }

                }
            }

            //This is the first scan
            //Return true
            return true;
            
        }

        private Boolean IsFolderModified(JObject fileObj,string directoryPath)
        {
            //Fetch all files in this directory
            string[] filePaths = FileSystemHelpers.GetFiles(directoryPath,"*");

            foreach(string filePath in filePaths)
            {
                //If manifest does not contain an entry for this file
                //It means the file was newly added
                //We need to scan as this is a modification
                if (!fileObj.ContainsKey(filePath))
                {
                    return true;
                }
                //Modified time in manifest
                String lastModTime = (string)fileObj[filePath];
                //Current modified time
                String currModTime = FileSystemHelpers.GetDirectoryLastWriteTimeUtc(filePath).ToString();

                //If they are different
                //It means file has been modified after last scan
                if (!currModTime.Equals(lastModTime))
                    return true;
            }

            //Fetch all the child directories of this directory
            string[] direcPaths = FileSystemHelpers.GetDirectories(directoryPath);

            //Do recursive comparison of all files in the child directories
            foreach(string direcPath in direcPaths)
            {
                if (IsFolderModified(fileObj, direcPath))
                    return true;
            }

            //No modifications found
            return false;
        }

        private void ModifyManifestFile(JObject fileObj, string directoryPath)
        {
            //Get all files in this directory
            string[] filePaths = FileSystemHelpers.GetFiles(directoryPath, "*");

            foreach (string filePath in filePaths)
            {
                //Get last modified timestamp of this file
                String timeString = FileSystemHelpers.GetDirectoryLastWriteTimeUtc(filePath).ToString();
                //Add it as an entry into the manifest
                fileObj.Add(filePath, timeString);
            }

            //Get all child directories of this directory
            string[] direcPaths = FileSystemHelpers.GetDirectories(directoryPath);
            //Do a recursive call to add files of child directories to manifest
            foreach (string direcPath in direcPaths)
            {
                ModifyManifestFile(fileObj, direcPath);                 
            }

        }

        public async Task<ScanRequestResult> StartScan(String timeout,String mainScanDirPath,String id)
        {
            using (_tracer.Step("Start scan in the background"))
            {
                String folderPath = Path.Combine(mainScanDirPath, Constants.ScanFolderName + id);
                String filePath = Path.Combine(folderPath, Constants.ScanStatusFile);
                Boolean hasFileModifcations = true;

                //Create unique scan folder and scan status file
                _scanLock.LockOperation(() =>
                {
                    //Check if files are modified
                    if (CheckModifications(mainScanDirPath))
                    {
                        //Create unique scan directory for current scan
                        FileSystemHelpers.CreateDirectory(folderPath);
                        _tracer.Trace("Unique scan directory created for scan {0}", id);

                        //Create scan status file inside folder
                        FileSystemHelpers.CreateFile(filePath).Close();

                        //Create temp file to check if scan is still running
                        string tempScanFilePath = GetTempScanFilePath(mainScanDirPath);
                        tempScanFilePath = Path.Combine(mainScanDirPath, Constants.TempScanFile);
                        FileSystemHelpers.CreateFile(tempScanFilePath).Close();

                        UpdateScanStatus(folderPath, ScanStatus.Starting);
                    }
                    else
                    {
                        
                        hasFileModifcations = false;
                    }
                    

                }, "Creating unique scan folder", TimeSpan.Zero);

                if (!hasFileModifcations)
                {
                    return ScanRequestResult.NoFileModifications;
                }

                //Start Backgorund Scan
                using (var timeoutCancellationTokenSource = new CancellationTokenSource())
                {
                    var successfullyScanned = PerformBackgroundScan(_tracer, _scanLock, folderPath, timeoutCancellationTokenSource.Token,id,mainScanDirPath);

                    //Wait till scan task completes or the timeout goes off
                    if (await Task.WhenAny(successfullyScanned, Task.Delay(Int32.Parse(timeout), timeoutCancellationTokenSource.Token)) == successfullyScanned)
                    {
                        //If scan task completes before timeout
                        //Delete excess scan folders, just keep the maximum number allowed
                        await DeletePastScans(mainScanDirPath, _tracer);

                        //Create new Manifest file containing the modified timestamps
                        String manifestPath = Path.Combine(mainScanDirPath, Constants.ScanManifest);
                        if (FileSystemHelpers.FileExists(manifestPath))
                        {
                            FileSystemHelpers.DeleteFileSafe(manifestPath);
                        }
                        JObject manifestObj = new JObject();

                        //Write to the manifest with new timestamps of the modified file
                        ModifyManifestFile(manifestObj, Constants.ScanDir);
                        File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifestObj));

                        //Path to common log file for azure monitor
                        String aggrLogPath = Path.Combine(mainScanDirPath, Constants.AggregrateScanResults);

                        //This checks if result scan log is formed
                        //If yes, it will append necessary logs to the aggregrate log file
                        //Current appended logs will be "Scanned files","Infected files", and details of infected files
                        String currLogPath = Path.Combine(folderPath, Constants.ScanLogFile);
                        if (FileSystemHelpers.FileExists(currLogPath))
                        {
                            StreamReader file = new StreamReader(currLogPath);
                            string line;
                            while ((line = file.ReadLine()) != null)
                            {
                                if (line.Contains("FOUND") || line.Contains("Infected files") || line.Contains("Scanned files"))
                                {
                                    //logType 0 means this log line represents details of infected files
                                    String logType = "0";
                                    if (line.Contains("Infected files") || line.Contains("Scanned files"))
                                    {
                                        //logType 1 means this log line represents total number of scanned or infected files
                                        logType = "1";
                                    }
                                    FileSystemHelpers.AppendAllTextToFile(aggrLogPath, DateTime.UtcNow.ToString(@"M/d/yyyy hh:mm:ss tt") + "," + id + "," + logType + "," + line + '\n');
                                }
                            }
                        }

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
                        await DeletePastScans(mainScanDirPath, _tracer);

                        return ScanRequestResult.AsyncScanFailed;
                        
                    }
                }

                
            }

        }

        public static async Task DeletePastScans(String mainDirectory, ITracer _tracer)
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
                        subDirs[i].Delete(true);
                        _tracer.Trace("Deleted scan record folder {0}",subDirs[i].FullName);
                    }
                }
            });

        }

        public async Task<ScanStatusResult> GetScanStatus(String scanId,String mainScanDirPath)
        {
            ScanStatusResult obj = null;
            await Task.Run(() =>
            {
                obj = ReadScanStatusFile(scanId, mainScanDirPath, Constants.ScanStatusFile,null);
            });

                return obj;
        }

        public async Task<ScanReport> GetScanResultFile(String scanId,String mainScanDirPath)
        {
            //JObject statusRes = await GetScanStatus(scanId, mainScanDirPath);
            ScanReport report = null;
            //Run task to read the result file
            await Task.Run(() =>
            {
                String report_path = Path.Combine(mainScanDirPath, Constants.ScanFolderName + scanId, Constants.ScanLogFile);
                ScanStatusResult scr = ReadScanStatusFile(scanId, mainScanDirPath, Constants.ScanStatusFile, null);

                //Proceed only if this scan has actually been conducted
                //Handling possibility of user entering invalid scanId and breaking the application
                if(scr != null)
                {
                    report = new ScanReport();
                    report.Id = scr.Id;
                    report.Timestamp = DateTime.ParseExact(scr.Id, DATE_TIME_FORMAT, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
                    String text = "Report file not yet generated. The scan might be still running, please check the status";

                    if (FileSystemHelpers.FileExists(report_path))
                    {
                        text = FileSystemHelpers.ReadAllTextFromFile(report_path);
                    }

                    report.Report = text;
                }
            });

            //All the contents of the file and the timestamp
                return report;
        }

        private static ScanStatusResult ReadScanStatusFile(String scanId, String mainScanDirPath, String fileName,String folderName)
        {
            ScanStatusResult obj = null;
            String readPath = Path.Combine(mainScanDirPath, Constants.ScanFolderName + scanId, fileName);

            //Give preference to folderName if given
            if(folderName != null)
            {
                readPath = Path.Combine(folderName, fileName);
            }

            //Check if scan status file has been formed
            if (FileSystemHelpers.FileExists(readPath))
            {
                //Read json file and deserialize into JObject
                using (FileStream file = System.IO.File.OpenRead(readPath))
                {
                    using (StreamReader sr = new StreamReader(file))
                    {
                        JsonSerializer serializer = new JsonSerializer();
                        obj = (ScanStatusResult)serializer.Deserialize(sr, typeof(ScanStatusResult));
                    }

                }
            }
           
            return obj;
        }

        public void StopScan(String mainScanDirPath)
        {
            string tempScanFilePath = GetTempScanFilePath(mainScanDirPath);
            if (tempScanFilePath != null && FileSystemHelpers.FileExists(tempScanFilePath))
            {
                FileSystemHelpers.DeleteFileSafe(tempScanFilePath);
                _tracer.Trace("Scan is being stopped. Deleted temp scan file at {0}",tempScanFilePath);
            }
        }

        private string GetTempScanFilePath(String mainScanDirPath)
        {
            return Path.Combine(mainScanDirPath, Constants.TempScanFile);
        }

        public async Task<bool> PerformBackgroundScan(ITracer _tracer, IOperationLock _scanLock, String folderPath,CancellationToken token, String scanId, String mainScanDirPath)
        {

            var successfulScan = true;

            await Task.Run(() =>
            {

                _scanLock.LockOperation(() =>
                {

                    String statusFilePath = Path.Combine(folderPath, Constants.ScanStatusFile);


                    String logFilePath = Path.Combine(folderPath, Constants.ScanLogFile);
                    _tracer.Trace("Starting Scan {0}, ScanCommand: {1}, LogFile: {2}",scanId,Constants.ScanCommand,logFilePath);

                    UpdateScanStatus(folderPath, ScanStatus.Executing);
                    
                    var escapedArgs = Constants.ScanCommand + " " + logFilePath;
                    Process _executingProcess = new Process()
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "/bin/bash",
                            Arguments = "-c \"" + escapedArgs + "\"",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        }
                    };
                    _executingProcess.Start();

                    string tempScanFilePath = GetTempScanFilePath(mainScanDirPath);
                    //Check if process is completing before timeout
                    while (!_executingProcess.HasExited)
                    {
                        //Process still running, but timeout is done
                        //Or Process is still running but scan has been stopped by user
                        if (token.IsCancellationRequested || (tempScanFilePath != null && !FileSystemHelpers.FileExists(tempScanFilePath)))
                        {
                            //Kill process
                            _executingProcess.Kill(true, _tracer);
                            //Wait for process to be completely killed
                            _executingProcess.WaitForExit();
                            successfulScan = false;
                            if (token.IsCancellationRequested)
                            {
                                _tracer.Trace("Scan {0} has timed out at {1}", scanId, DateTime.UtcNow.ToString("yyy-MM-dd_HH-mm-ssZ"));

                                //Update status file
                                UpdateScanStatus(folderPath, ScanStatus.TimeoutFailure);
                            }
                            else
                            {
                                _tracer.Trace("Scan {0} has been force stopped at {1}", scanId, DateTime.UtcNow.ToString("yyy-MM-dd_HH-mm-ssZ"));

                                //Update status file
                                UpdateScanStatus(folderPath, ScanStatus.ForceStopped);
                            }
                            
                            break;
                        }
                    }

                    //Clean up the temp file
                    StopScan(mainScanDirPath);

                    //Update status file with success
                    if (successfulScan)
                    {
                        //Check if process terminated with errors
                        if(_executingProcess.ExitCode != 0)
                        {
                            UpdateScanStatus(folderPath, ScanStatus.Failed);
                            _tracer.Trace("Scan {0} has terminated with exit code {1}. More info found in {2}", scanId, _executingProcess.ExitCode, logFilePath);
                        }
                        else
                        {
                            UpdateScanStatus(folderPath, ScanStatus.Success);
                            _tracer.Trace("Scan {0} is Successful", scanId);
                        }
                    }


                }, "Performing continuous scan", TimeSpan.Zero);



            });

            return successfulScan;
           
        }

        public IEnumerable<ScanOverviewResult> GetResults(String mainScanDir)
        {
                IEnumerable<ScanOverviewResult> results = EnumerateResults(mainScanDir).OrderByDescending(t => t.Status.Id).ToList();
                return results;
        }

        private IEnumerable<ScanOverviewResult> EnumerateResults(String mainScanDir)
        {
            if (FileSystemHelpers.DirectoryExists(mainScanDir))
            {
                
                foreach (String scanFolderPath in FileSystemHelpers.GetDirectories(mainScanDir))
                {
                    ScanOverviewResult result = new ScanOverviewResult();
                    ScanStatusResult scanStatus = ReadScanStatusFile("", "", Constants.ScanStatusFile, scanFolderPath);
                    result.Status = scanStatus;

                    yield return result;
                }
            }
        }

    }
}
