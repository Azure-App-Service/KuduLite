using Kudu.Core.Deployment;
using Kudu.Core.Infrastructure;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
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
        [HttpGet]
        public IActionResult RedployDeployemnt([FromRoute] string appName, [FromRoute] string deploymentId)
        {
            try {

            System.Console.WriteLine("Restarting Pods for App Service App : " + appName);
            System.Console.WriteLine($" Patch Args :::::: -c \" /patch.sh {appName} apps/{appName}/site/artifacts/{deploymentId}\"");

            Process _executingProcess = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \" /patch.sh {appName} apps/{appName}/site/artifacts/{deploymentId}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            // Read the standard error of net.exe and write it on to console.
            _executingProcess.OutputDataReceived += (sender, args) => System.Console.WriteLine("{0}", args.Data);
            _executingProcess.Start();
            _executingProcess.WaitForExit();
            System.Console.WriteLine("Process exit code : " + _executingProcess.ExitCode);
            System.Console.WriteLine("All Pods Restarted!");
            FileSystemHelpers.WriteAllText($"/home/apps/{appName}/site/artifacts/active", deploymentId);
            }
            catch (Exception e)
            {
                Console.WriteLine("error : "+e.Message);
            }
            return Ok();
        }
    }
}
