using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Kudu.Core.Deployment;
using Kudu.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kudu.Services.Web
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
            public IDeploymentStatusFile status;
        }

        // GET api/values
        [HttpGet("{appName=all}")]
        public IActionResult GetAllRevisions([FromRoute] string appName = "all")
        {
            List<revisiondata> ret = new List<revisiondata>();
            string active = "";
            if (FileSystemHelpers.FileExists($"/home/apps/{appName}/Site/artifacts/active"))
            {
                active = FileSystemHelpers.ReadAllText($"/home/apps/{appName}/Site/artifacts/active");
            }

            if (FileSystemHelpers.DirectoryExists($"/home/apps/{appName}"))
            {
                foreach(var dir in FileSystemHelpers.GetDirectories($"/home/apps/{appName}/Site/artifacts"))
                {
                    string rev = "";
                   if( FileSystemHelpers.FileExists($"{dir}/revision"))
                   {
                        rev = FileSystemHelpers.ReadAllText($"{dir}/revision");
                   }
                    bool isCurr = active.Equals(dir.Replace($"/home/apps/{appName}/Site/artifacts/", ""));
                    //string status2 = FileSystemHelpers.ReadAllText($"{dir}/metadata.json");
                    ret.Add(new revisiondata() { active = isCurr, revisionId = rev });
                }
            }
            return Ok(ret);
        }

        // GET api/values/5
        [HttpPost("revision/{id}")]
        public ActionResult<string> Get(int id)
        {
            return "value";
        }

        // POST api/values
        [HttpPost("revision/{id}")]
        public void Post([FromBody] string value)
        {
        }

        // PUT api/values/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/values/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
