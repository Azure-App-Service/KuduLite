using Kudu.Core.Deployment;
using Kudu.Core.Infrastructure;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Kudu.Services.Diagnostics
{
    public class RevisionController : Controller
    {
        public class revisiondata
        {
            public string revisionId;
            public string deploymentId;
            public bool active;
            public DeployStatus Status { get; set; }
            public string StatusText { get; set; }
            public string AuthorEmail { get; set; }
            public string Author { get; set; }
            public string Message { get; set; }
            public string Deployer { get; set; }
            public DateTime ReceivedTime { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
        }

        [EnableCors]
        [HttpGet]
        public IActionResult GetMyRevisions([FromRoute] string appName = "all")
        {
            List<revisiondata> ret = new List<revisiondata>();
            string active = "";
            if (FileSystemHelpers.FileExists($"/home/apps/{appName}/site/artifacts/current"))
            {
                active = FileSystemHelpers.ReadAllText($"/home/apps/{appName}/site/artifacts/current");
            }

            if (FileSystemHelpers.DirectoryExists($"/home/apps/{appName}"))
            {
                foreach (var dir in FileSystemHelpers.GetDirectories($"/home/apps/{appName}/site/artifacts"))
                {
                    string rev = "";
                    if (FileSystemHelpers.FileExists($"{dir}/revision"))
                    {
                        rev = FileSystemHelpers.ReadAllText($"{dir}/revision");
                    }
                    bool isCurr = active.Equals(dir.Replace($"/home/apps/{appName}/site/artifacts/", ""));
                    string status2 = FileSystemHelpers.ReadAllText($"{dir}/metadata.json");
                    var revData = JsonConvert.DeserializeObject<revisiondata>(status2);
                    ret.Add(new revisiondata() { deploymentId = revData.deploymentId ,active = isCurr, revisionId = rev, EndTime = revData.EndTime, Deployer = revData.Deployer, Author = revData.Author, AuthorEmail = revData.AuthorEmail, ReceivedTime = revData.ReceivedTime, StartTime = revData.StartTime, Status = revData.Status, Message = revData.Message });
                }
            }
            return Ok(ret);
        }

        public class RevisionPost
        {
            public string appName;
            public string deploymentId;
        }

        [EnableCors]
        [HttpPost]
        public IActionResult RedployDeployemnt([FromForm] RevisionPost rev)
        {
            try { 
            //var rev = Newtonsoft.Json.JsonConvert.DeserializeObject<RevisionPost>(
             //                   jsonData.ToString(Formatting.None));
            System.Console.WriteLine("Restarting Pods for App Service App : " + rev.appName);
            System.Console.WriteLine($" Patch Args :::::: -c \" /patch.sh {rev.appName} apps/{rev.appName}/site/artifacts/{rev.deploymentId}\"");

            Process _executingProcess = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \" /patch.sh {rev.appName} apps/{rev.appName}/site/artifacts/{rev.deploymentId}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            // Read the standard error of net.exe and write it on to console.
            _executingProcess.OutputDataReceived += (sender, args) => System.Console.WriteLine("{0}", args.Data);
            _executingProcess.Start();
            //* Read the output (or the error)
            //string output = _executingProcess.StandardOutput.ReadToEnd();
            //System.Console.WriteLine(output);
            //string err = _executingProcess.StandardError.ReadToEnd();
            //System.Console.WriteLine(err);
            _executingProcess.WaitForExit();
            System.Console.WriteLine("Process exit code : " + _executingProcess.ExitCode);
            System.Console.WriteLine("All Pods Restarted!");
            FileSystemHelpers.WriteAllText($"/home/apps/{rev.appName}/site/artifacts/active", rev.deploymentId);
            }
            catch (Exception e)
            {

            }
            return Ok();
        }
    }
}
