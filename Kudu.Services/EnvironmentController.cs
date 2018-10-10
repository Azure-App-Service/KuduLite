using System.IO;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc;

namespace Kudu.Services
{
    public class EnvironmentController : Controller
    {
        private static readonly string _version = typeof(EnvironmentController).Assembly.GetName().Version.ToString();

        /// <summary>
        /// Get the Kudu version
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public IActionResult Get()
        {
            // Return the version and other api information (in the end)
            // { 
            //   "version" : "1.0.0",
            //   "siteLastModified" : "2015-02-11T18:27:22.7270000Z",
            // }
            var obj = new JObject(new JProperty("version", _version));

            // this file is written by dwas to communicate the last site configuration modified time
            var lastModifiedFile = Path.Combine(
                Path.GetDirectoryName(System.Environment.GetEnvironmentVariable("TMP")),
                @"config\SiteLastModifiedTime.txt");
            if (System.IO.File.Exists(lastModifiedFile))
            {
                obj["siteLastModified"] = System.IO.File.ReadAllText(lastModifiedFile);
            }

            return Json(obj);
        }
    }
}
