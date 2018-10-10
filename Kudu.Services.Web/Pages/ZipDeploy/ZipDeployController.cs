using Kudu.Core.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace Kudu.Services.Web.Pages.ZipDeploy
{
    public class ZipDeployController : Controller
    {
        public ActionResult Index()
        {
            var os = OSDetector.IsOnWindows() ? "Windows" : "Linux";
            return View($"~/Pages/ZipDeploy/{os}ZipDeploy.cshtml");
        }

        public ActionResult LinuxZipDeploy()
        {
            return View($"~/Pages/ZipDeploy/LinuxZipDeploy.cshtml");
        }

        public ActionResult WindowsZipDeploy()
        {
            return View($"~/Pages/ZipDeploy/WindowsZipDeploy.cshtml");
        }
    }
}