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
        private ExternalCommandFactory _externalCommandFactory = null;
        private IEnvironment _environment;
        private static readonly string DATE_TIME_FORMAT = "yyyy-MM-dd_HH-mm-ssZ";

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

        public async Task<ScanRequestResult> StartScan(String timeout,String mainScanDirPath,String timestamp)
        {
            using (_tracer.Step("Start scan in the background"))
            {
                String folderPath = Path.Combine(mainScanDirPath, Constants.ScanFolderName + timestamp);
                String filePath = Path.Combine(folderPath, Constants.ScanStatusFile);
                Boolean hasFileModifcations = true;

                //Create unique scan folder and scan status file
                _scanLock.LockOperation(() =>
                {
                    //Check if files are modified
                    if (CheckModifications(mainScanDirPath))
                    {
                        //Create uniue scan directory for current scan
                        FileSystemHelpers.CreateDirectory(folderPath);
                        Console.WriteLine("Unique scan directory created");

                        //Create scan status file inside folder
                        FileSystemHelpers.CreateFile(filePath).Close();

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
                    var successfullyScanned = PerformBackgroundScan(_tracer, _scanLock, folderPath, _externalCommandFactory,_environment,timeoutCancellationTokenSource.Token);

                    //Wait till scan task completes or the timeout goes off
                    if (await Task.WhenAny(successfullyScanned, Task.Delay(Int32.Parse(timeout), timeoutCancellationTokenSource.Token)) == successfullyScanned)
                    {
                        Console.WriteLine("No Timeout!!");
                        //If scan task completes before timeout
                        //Delete excess scan folders, just keep the maximum number allowed
                        await DeletePastScans(mainScanDirPath);

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

                        //Create common log file for azure monitor
                        String aggrLogPath = Path.Combine(mainScanDirPath, Constants.AggregrateScanResults);
                        if (!FileSystemHelpers.FileExists(aggrLogPath))
                        {
                            FileSystemHelpers.CreateFile(aggrLogPath);
                        }

                        String currLogPath = Path.Combine(folderPath, Constants.ScanLogFile);
                        Boolean summaryStart = false;
                        if (FileSystemHelpers.FileExists(currLogPath))
                        {
                            StreamReader file = new StreamReader(currLogPath);
                            string line;
                            File.AppendAllText(aggrLogPath, "------- NEW SCAN REPORT -------" + '\n');
                            while ((line = file.ReadLine()) != null)
                            {
                                if (line.Contains("FOUND") || summaryStart || line.Contains("SCAN SUMMARY"))
                                {
                                    if(line.Contains("SCAN SUMMARY"))
                                    {
                                        summaryStart = true;
                                    }
                                    File.AppendAllText(aggrLogPath, line+'\n');
                                }
                                //else if(summaryStart || )
                            }
                        }

                        return successfullyScanned.Result
                        ? ScanRequestResult.RunningAynschronously
                        : ScanRequestResult.AsyncScanFailed;
                    }
                    else
                    {
                        Console.WriteLine("Timeout!!");
                        //Timeout went off before scan task completion
                        //Cancel scan task
                        timeoutCancellationTokenSource.Cancel();

                        //Scan process will be cancelled
                        //wait till scan status file is appropriately updated
                        await successfullyScanned;
                       
                        //Delete excess scan folders, just keep the maximum number allowed
                        await DeletePastScans(mainScanDirPath);

                        return ScanRequestResult.AsyncScanFailed;
                        
                    }
                }

                
            }

        }

        public static async Task DeletePastScans(String mainDirectory)
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
                report = new ScanReport();
                report.Id = scr.Id;
                report.Timestamp = DateTime.ParseExact(scr.Id,DATE_TIME_FORMAT, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
                String text = "Report file not yet generated. The scan might be still running, please check the status";

                if (FileSystemHelpers.FileExists(report_path))
                {
                    text = FileSystemHelpers.ReadAllTextFromFile(report_path);
                }

                report.Report = text;
                
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

        public static async Task<bool> PerformBackgroundScan(ITracer _tracer, IOperationLock _scanLock, String folderPath,ExternalCommandFactory _externalCommandFactory,IEnvironment _environment,CancellationToken token)
        {

            var successfulScan = true;

            await Task.Run(() =>
            {

                _scanLock.LockOperation(() =>
                {

                    String filePath = Path.Combine(folderPath, Constants.ScanStatusFile);


                    String logFilePath = Path.Combine(folderPath, Constants.ScanLogFile);
                    Console.WriteLine("Starting Command Executor:" + Constants.ScanCommand + " " + logFilePath);

                    UpdateScanStatus(folderPath, ScanStatus.Executing);

                    Console.WriteLine("Before process start");

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

                    Console.WriteLine("Process exit:" + _executingProcess.HasExited);
                    //Check if process is completing before timeout
                    while (!_executingProcess.HasExited)
                    {
                        //Process still running, but timeout is done
                        if (token.IsCancellationRequested)
                        {

                            Console.WriteLine("Cancel Requested Inside!!");
                            //Kill process
                            _executingProcess.Kill(true, _tracer);
                            Console.WriteLine("After Kill!!");
                            //Wait for process to be completely killed
                            _executingProcess.WaitForExit();
                            successfulScan = false;
                            Console.WriteLine("Token Cancellation requested! Terminating process! Time: " + DateTime.UtcNow.ToString("yyy-MM-dd_HH-mm-ssZ"));

                            //Update status file
                            UpdateScanStatus(folderPath, ScanStatus.TimeoutFailure);
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
           
        }

        public IEnumerable<ScanOverviewResult> GetResults(String mainScanDir)
        {
           // ITracer tracer = _tracer.GetTracer();
           /* using (_tracer.Step("ScanManager.GetResults"))
            {*/
                IEnumerable<ScanOverviewResult> results = EnumerateResults(mainScanDir).OrderByDescending(t => t.Status.Id).ToList();
                return results;
                //return PurgeAndGetDeployments();
           /* }*/
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
