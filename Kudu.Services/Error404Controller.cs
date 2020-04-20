using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc;
using System;

namespace Kudu.Services
{
    public class Error404Controller : Controller
    {
        [HttpGet, HttpPatch, HttpPost, HttpPut, HttpDelete]
        public virtual Task<IActionResult> Handle()
        {
            // Mock few paths. For development purposes only.
            if (this.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                if (this.Request.Path.Equals(Constants.RestartApiPath, System.StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult((IActionResult) Ok());
                }
            }

            return Task.FromResult((IActionResult) NotFound($"No route registered for '{this.Request.Path}'"));
        }
    }
}