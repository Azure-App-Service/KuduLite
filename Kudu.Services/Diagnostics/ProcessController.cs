using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Services.Diagnostics
{
    public class ProcessController : Controller
    {
        [HttpGet]
        public IActionResult GetAllProcesses()
        {
            return new JsonResult("Waiting to get all windows processes ...");
        }
    }
}
