using Kudu.Contracts.Tracing;
using Kudu.Core;
using LibGit2Sharp;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Kudu.Services
{
    /// <summary>
    /// fileUpload Controller
    /// </summary>
    public class fileUploadController : Controller
    {
        private IEnvironment _webAppRuntimeEnvironment;
        private IFileProvider _fileProvider;

        /// <summary>
        /// fileUpload Controller Class constructor
        /// </summary>
        public fileUploadController(IEnvironment webAppRuntimeEnvironment, IFileProvider fileProvider)
        {
            _webAppRuntimeEnvironment = webAppRuntimeEnvironment;
            _fileProvider = fileProvider;
        }

        /// <summary>
        /// Get List of files
        /// </summary>
        /// <returns></returns>
        public List<FileDetails> Files()
        {
            var model = new List<FileDetails>();
            foreach (var item in _fileProvider.GetDirectoryContents(""))
            {
                model.Add(
                    new FileDetails { Name = item.Name, Path = item.PhysicalPath });
            }
            ViewData["hdnFlag"] = model;
            return model;
        }

        /// <summary>
        /// Uploads file to root path
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        public async Task<IActionResult> UploadFile([FromForm] FileInputModel model)
        {
            if (model.FileToUpload == null || model.FileToUpload.Count == 0)
                return Content("file not selected");
            if(string.IsNullOrEmpty(model.Name))
            {
                model.Name = string.Empty;
            }
            //****Will have to change the file path to relative URL****
            foreach (IFormFile file in model.FileToUpload)
            {
                var path = Path.Combine(
                            _webAppRuntimeEnvironment.RootPath + model.Name,
                            file.FileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
            }
            return Redirect("../newui/fileManager");
        }

        public class FileDetails
        {
            public string Name { get; set; }
            public string Path { get; set; }
        }

        /// <summary>
        /// Model Required to Upload files along with the current Path (Name).
        /// </summary>
        /// <returns></returns>
        public class FileInputModel
        {
            public string Name { get; set; }
            public List<IFormFile> FileToUpload { get; set; }
        }
    }
}
