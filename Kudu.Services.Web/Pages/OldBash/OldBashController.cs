using Microsoft.AspNetCore.Mvc;
using Kudu.Core.Helpers;

namespace Kudu.Services.Web.Pages
{
    // CORE NOTE This is a new shim to get the right console to load; couldn't do it the way it was originally done
    // due to the differences in the way the new razor pages work
    public class OldBashController : Controller
    {
        public ActionResult Index()
        {
            var os = OSDetector.IsOnWindows() ? "Windows" : "Linux";
            return View($"~/Pages/OldBash/{os}BashConsole.cshtml");
        }
    }
}