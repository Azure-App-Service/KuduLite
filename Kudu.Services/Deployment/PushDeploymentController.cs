using System;
using System.IO.Compression;
using System.Threading.Tasks;
using Kudu.Contracts.Tracing;
using Kudu.Core.Deployment;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Kudu.Services.Infrastructure;
using System.IO;
using Kudu.Core.SourceControl;
using System.Globalization;
using Kudu.Core;
using System.Linq;
using System.Net.Http;
using Kudu.Services.Util;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Kudu.Services.Deployment
{
    public class PushDeploymentController : Controller
    {
        private const string DefaultDeployer = "Zip-Push";
        private const string DefaultMessage = "Created via zip push deployment";

        private readonly IEnvironment _environment;
        private readonly IFetchDeploymentManager _deploymentManager;
        private readonly ITracer _tracer;
        private readonly ITraceFactory _traceFactory;

        public PushDeploymentController(
            IEnvironment environment,
            IFetchDeploymentManager deploymentManager,
            ITracer tracer,
            ITraceFactory traceFactory)
        {
            _environment = environment;
            _deploymentManager = deploymentManager;
            _tracer = tracer;
            _traceFactory = traceFactory;
        }

        [HttpPost]
        [DisableRequestSizeLimit]
        [DisableFormValueModelBinding]
        public async Task<IActionResult> ZipPushDeploy(
            bool isAsync = false,
            string author = null,
            string authorEmail = null,
            string deployer = DefaultDeployer,
            string message = DefaultMessage)
        {
            using (_tracer.Step("ZipPushDeploy"))
            {
                var zipFilePath = Path.Combine(_environment.ZipTempPath, Guid.NewGuid() + ".zip");
                //var zipFileName = Path.ChangeExtension(Path.GetRandomFileName(), "zip");
                //var zipFilePath = Path.Combine(_environment.ZipTempPath, zipFileName);
                FormValueProvider formModel;
                using (_tracer.Step("Writing zip file to {0}", zipFilePath))
                {
                    using (var file = System.IO.File.Create(zipFilePath))
                    {
                        formModel = await Request.StreamFile(file);
                    }
                }
                 
                /*
                var bindingSuccessful = await TryUpdateModelAsync(viewModel, prefix: "",
                    valueProvider: formModel);
 
                if (!bindingSuccessful)
                {
                    if (!ModelState.IsValid)
                    {
                        return BadRequest(ModelState);
                    }
                }
                */
                 
                var deploymentInfo = new ZipDeploymentInfo(_environment, _traceFactory)
                {
                    AllowDeploymentWhileScmDisabled = true,
                    Deployer = deployer,
                    IsContinuous = false,
                    AllowDeferredDeployment = false,
                    IsReusable = false,
                    RepositoryUrl = zipFilePath,
                    TargetChangeset = DeploymentManager.CreateTemporaryChangeSet(message: "Deploying from pushed zip file"),
                    CommitId = null,
                    RepositoryType = RepositoryType.None,
                    Fetch = LocalZipFetch,
                    DoFullBuildByDefault = false,
                    Author = author,
                    AuthorEmail = authorEmail,
                    Message = message
                };

                var result = await _deploymentManager.FetchDeploy(deploymentInfo, isAsync, UriHelper.GetRequestUri(Request), "HEAD");

                switch (result)
                {
                    case FetchDeploymentRequestResult.RunningAynschronously:
                        if (isAsync)
                        {
                            // latest deployment keyword reserved to poll till deployment done
                            Response.GetTypedHeaders().Location =
                                new Uri(UriHelper.GetRequestUri(Request),
                                String.Format("/api/deployments/{0}?deployer={1}&time={2}", Constants.LatestDeployment, deploymentInfo.Deployer, DateTime.UtcNow.ToString("yyy-MM-dd_HH-mm-ssZ")));
                        }
                        return Accepted();
                    case FetchDeploymentRequestResult.ForbiddenScmDisabled:
                        // Should never hit this for zip push deploy
                        _tracer.Trace("Scm is not enabled, reject all requests.");
                        return Forbid();
                    case FetchDeploymentRequestResult.ConflictAutoSwapOngoing:
                        return StatusCode(StatusCodes.Status409Conflict, Resources.Error_AutoSwapDeploymentOngoing);
                    case FetchDeploymentRequestResult.Pending:
                        // Shouldn't happen here, as we disallow deferral for this use case
                        return Accepted();
                    case FetchDeploymentRequestResult.RanSynchronously:
                        return Ok();
                    case FetchDeploymentRequestResult.ConflictDeploymentInProgress:
                        return StatusCode(StatusCodes.Status409Conflict, Resources.Error_DeploymentInProgress);
                    default:
                        return BadRequest();
                }
            }
        }

        private async Task LocalZipFetch(IRepository repository, DeploymentInfoBase deploymentInfo, string targetBranch, ILogger logger, ITracer tracer)
        {
            var zipDeploymentInfo = (ZipDeploymentInfo)deploymentInfo;

            // For this kind of deployment, RepositoryUrl is a local path.
            var sourceZipFile = zipDeploymentInfo.RepositoryUrl;
            var extractTargetDirectory = repository.RepositoryPath;

            var info = FileSystemHelpers.FileInfoFromFileName(sourceZipFile);
            var sizeInMb = (info.Length / (1024f * 1024f)).ToString("0.00", CultureInfo.InvariantCulture);

            var message = String.Format(
                CultureInfo.InvariantCulture,
                "Cleaning up temp folders from previous zip deployments and extracting pushed zip file {0} ({1} MB) to {2}",
                info.FullName,
                sizeInMb,
                extractTargetDirectory);

            logger.Log(message);

            using (tracer.Step(message))
            {
                // If extractTargetDirectory already exists, rename it so we can delete it concurrently with
                // the unzip (along with any other junk in the folder)
                var targetInfo = FileSystemHelpers.DirectoryInfoFromDirectoryName(extractTargetDirectory);
                if (targetInfo.Exists)
                {
                    var moveTarget = Path.Combine(targetInfo.Parent.FullName, Path.GetRandomFileName());
                    targetInfo.MoveTo(moveTarget);
                }

                var cleanTask = Task.Run(() => DeleteFilesAndDirsExcept(sourceZipFile, extractTargetDirectory, tracer));
                var extractTask = Task.Run(() =>
                {
                    FileSystemHelpers.CreateDirectory(extractTargetDirectory);

                    using (var file = info.OpenRead())
                    using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                    {
                        zip.Extract(extractTargetDirectory);
                    }
                });

                await Task.WhenAll(cleanTask, extractTask);
            }

            // Needed in order for repository.GetChangeSet() to work.
            // Similar to what OneDriveHelper and DropBoxHelper do.
            repository.Commit(zipDeploymentInfo.Message, zipDeploymentInfo.Author, zipDeploymentInfo.AuthorEmail);
        }

        private void DeleteFilesAndDirsExcept(string fileToKeep, string dirToKeep, ITracer tracer)
        {
            // Best effort. Using the "Safe" variants does retries and swallows exceptions but
            // we may catch something non-obvious.
            try
            {
                var files = FileSystemHelpers.GetFiles(_environment.ZipTempPath, "*")
                .Where(p => !PathUtilityFactory.Instance.PathsEquals(p, fileToKeep));

                foreach (var file in files)
                {
                    FileSystemHelpers.DeleteFileSafe(file);
                }

                var dirs = FileSystemHelpers.GetDirectories(_environment.ZipTempPath)
                    .Where(p => !PathUtilityFactory.Instance.PathsEquals(p, dirToKeep));

                foreach (var dir in dirs)
                {
                    FileSystemHelpers.DeleteDirectorySafe(dir);
                }
            }
            catch (Exception ex)
            {
                tracer.TraceError(ex, "Exception encountered during zip folder cleanup");
                throw;
            }
        }
    }
}
