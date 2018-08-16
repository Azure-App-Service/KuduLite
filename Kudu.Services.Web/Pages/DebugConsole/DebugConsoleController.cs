using Microsoft.AspNetCore.Mvc;
using Kudu.Core.Helpers;

namespace Kudu.Services.Web.Pages.DebugConsole
{
    // CORE NOTE This is a new shim to get the right console to load; couldn't do it the way it was originally done
    // due to the differences in the way the new razor pages work
    public class DebugConsoleController : Controller
    {
        public ActionResult Index()
        {
            var os = OSDetector.IsOnWindows() ? "Windows" : "Linux";
            return View($"~/Pages/DebugConsole/{os}Console.cshtml");
        }

        public ActionResult LinuxConsole()
        {
            return View($"~/Pages/DebugConsole/LinuxConsole.cshtml");
        }

        public ActionResult WindowsConsole()
        {
            return View($"~/Pages/DebugConsole/WindowsConsole.cshtml");
        }

    }
}