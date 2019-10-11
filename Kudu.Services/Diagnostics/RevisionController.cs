using Kudu.Core.Deployment;
using Kudu.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Services.Diagnostics
{
    [Route("api/revisions")]
    [ApiController]
    public class RevisionController : ControllerBase
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

        // GET api/values
        [HttpGet("{appName=all}")]
        public IActionResult GetAllRevisions([FromRoute] string appName = "all")
        {
            List<revisiondata> ret = new List<revisiondata>();
            string active = "";
            if (FileSystemHelpers.FileExists($"/home/apps/{appName}/site/artifacts/active"))
            {
                active = FileSystemHelpers.ReadAllText($"/home/apps/{appName}/site/artifacts/active");
            }

            if (FileSystemHelpers.DirectoryExists($"/home/apps/{appName}"))
            {
                foreach (var dir in FileSystemHelpers.GetDirectories($"/home/apps/{appName}/ite/artifacts"))
                {
                    string rev = "";
                    if (FileSystemHelpers.FileExists($"{dir}/revision"))
                    {
                        rev = FileSystemHelpers.ReadAllText($"{dir}/revision");
                    }
                    bool isCurr = active.Equals(dir.Replace($"/home/apps/{appName}/site/artifacts/", ""));
                    string status2 = FileSystemHelpers.ReadAllText($"{dir}/metadata.json");
                    var revData = JsonConvert.DeserializeObject<revisiondata>(status2);
                    ret.Add(new revisiondata() { active = isCurr, revisionId = rev, EndTime = revData.EndTime, Deployer = revData.Deployer, Author = revData.Author, AuthorEmail = revData.AuthorEmail, ReceivedTime = revData.ReceivedTime, StartTime = revData.StartTime, Status = revData.Status, Message = revData.Message });
                }
            }
            return Ok(ret);
        }

      
        
    }
}
