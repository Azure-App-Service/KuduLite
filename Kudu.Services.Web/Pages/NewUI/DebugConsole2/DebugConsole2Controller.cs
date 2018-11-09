using Microsoft.AspNetCore.Mvc;
using Kudu.Core.Helpers;

namespace Kudu.Services.Web.Pages.NewUI.DebugConsole2
{
    // CORE NOTE This is a new shim to get the right console to load; couldn't do it the way it was originally done
    // due to the differences in the way the new razor pages work
    public class DebugConsole2Controller : Controller
    {
        public ActionResult Index()
        {
            var os = OSDetector.IsOnWindows() ? "Windows" : "Linux";
            return View($"~/Pages/NewUI/DebugConsole2/{os}Console2.cshtml");
        }

        public ActionResult LinuxConsole()
        {
            return View($"~/Pages/NewUI/DebugConsole/LinuxConsole2.cshtml");
        }

        public ActionResult WindowsConsole()
        {
            return View($"~/Pages/NewUI/DebugConsole2/WindowsConsole2.cshtml");
        }

    }
}