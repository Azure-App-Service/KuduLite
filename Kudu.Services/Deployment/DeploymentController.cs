using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Settings;
using Kudu.Contracts.SourceControl;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Deployment;
using Kudu.Core.Helpers;
using Kudu.Core.Infrastructure;
using Kudu.Core.Settings;
using Kudu.Core.SourceControl;
using Kudu.Core.Tracing;
using Kudu.Services.Arm;
using Kudu.Services.Infrastructure;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Extensions;
using kUriHelper = Kudu.Services.Infrastructure.UriHelper;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Kudu.Services.Zip;
using System.IO.Compression;

namespace Kudu.Services.Deployment
{
    public class DeploymentController : Controller
    {
        private static DeploymentsCacheItem _cachedDeployments = DeploymentsCacheItem.None;

        private IEnvironment _environment;
        private readonly IAnalytics _analytics;
        private readonly IDeploymentManager _deploymentManager;
        private readonly IDeploymentStatusManager _status;
        private readonly IDeploymentSettingsManager _settings;
        private readonly ITracer _tracer;
        private readonly AllSafeLinuxLock _deploymentLock;
        private readonly IRepositoryFactory _repositoryFactory;

        public DeploymentController(ITracer tracer,
                                    IEnvironment environment,
                                    IAnalytics analytics,
                                    IDeploymentManager deploymentManager,
                                    IDeploymentStatusManager status,
                                    IDeploymentSettingsManager settings,
                                    //IOperationLock deploymentLock,
                                    IDictionary<string, IOperationLock> namedLocks,
                                    IRepositoryFactory repositoryFactory,
                                    IHttpContextAccessor accessor)
        {
            _tracer = tracer;
            _analytics = analytics;
            _deploymentManager = deploymentManager;
            _status = status;
            _settings = settings;
            _deploymentLock = (AllSafeLinuxLock) namedLocks["deployment"];
            _repositoryFactory = repositoryFactory;
            GetEnvironment(accessor, environment);
        }


        private void GetEnvironment(IHttpContextAccessor accessor, IEnvironment environment)
        {
            var context = accessor.HttpContext;
            if (!PostDeploymentHelper.IsK8Environment())
            {
                _environment = environment;
            }
            else
            {
                _environment = (IEnvironment) context.Items["environment"];
            }
        }

        /// <summary>
        /// Delete a deployment
        /// </summary>
        /// <param name="id">id of the deployment to delete</param>
        [HttpDelete]
        public IActionResult Delete(string id)
        {
            IActionResult result = Ok();
            using (_tracer.Step("DeploymentService.Delete"))
            {
                try
                {
                    _deploymentLock.LockOperation(() =>
                    {
                        try
                        {
                            _deploymentManager.Delete(id);
                        }
                        catch (DirectoryNotFoundException ex)
                        {
                            result = NotFound(ex);
                        }
                        catch (InvalidOperationException ex)
                        {
                            result = StatusCode(StatusCodes.Status409Conflict, ex);
                        }
                    }, "Deleting deployment", TimeSpan.Zero);
                }
                catch (LockOperationException ex)
                {
                    result = StatusCode(StatusCodes.Status409Conflict, ex.Message);
                }
            }

            return result;
        }

        
        [HttpGet]
        public IActionResult IsDeploying()
        {
            string msg = "";
            if (_deploymentLock.IsHeld)
            {
                msg = _deploymentLock.GetLockMsg();
            }

            return Json(new Dictionary<string, string>() { { "value", _deploymentLock.IsHeld.ToString() }, { "msg", msg } });
        }
        
        
        /// <summary>
        /// Deploy a previous deployment
        /// </summary>
        /// <param name="id">id of the deployment to redeploy</param>
        [HttpPut]
        public async Task<IActionResult> Deploy(string id = null)
        {
            JObject jsonContent = GetJsonContent();

            // Just block here to read the json payload from the body
            using (_tracer.Step("DeploymentService.Deploy(id)"))
            {
                IActionResult result = Ok();

                try
                {
                    await _deploymentLock.LockOperationAsync(async () =>
                    {
                        try
                        {
                            if (PostDeploymentHelper.IsAutoSwapOngoing())
                            {
                                result = StatusCode(StatusCodes.Status409Conflict, Resources.Error_AutoSwapDeploymentOngoing);
                                return;
                            }

                            DeployResult deployResult;
                            if (TryParseDeployResult(id, jsonContent, out deployResult))
                            {
                                using (_tracer.Step("DeploymentService.Create(id)"))
                                {
                                    CreateDeployment(deployResult, jsonContent.Value<string>("details"));

                                    // e.g if final url is "https://kudutry.scm.azurewebsites.net/api/deployments/ef52ec67fc9574e726955a9cbaf7bcba791e4e95/log"
                                    // deploymentUri should be "https://kudutry.scm.azurewebsites.net/api/deployments/ef52ec67fc9574e726955a9cbaf7bcba791e4e95"
                                    Uri deploymentUri = kUriHelper.MakeRelative(kUriHelper.GetBaseUri(Request), new Uri(Request.GetDisplayUrl()).AbsolutePath);
                                    deployResult.Url = deploymentUri;
                                    deployResult.LogUrl = kUriHelper.MakeRelative(deploymentUri, "log");

                                    // response = Request.CreateResponse(HttpStatusCode.OK, ArmUtils.AddEnvelopeOnArmRequest(deployResult, Request));
                                    result = Ok(ArmUtils.AddEnvelopeOnArmRequest(deployResult, Request));
                                    return;
                                }
                            }

                            bool clean = false;
                            bool needFileUpdate = true;

                            if (jsonContent != null)
                            {
                                clean = jsonContent.Value<bool>("clean");
                                JToken needFileUpdateToken;
                                if (jsonContent.TryGetValue("needFileUpdate", out needFileUpdateToken))
                                {
                                    needFileUpdate = needFileUpdateToken.Value<bool>();
                                }
                            }

                            string username = null;
                            AuthUtility.TryExtractBasicAuthUser(Request, out username);

                            IRepository repository = _repositoryFactory.GetRepository();
                            if (repository == null)
                            {
                                result = NotFound(Resources.Error_RepositoryNotFound);
                                return;
                            }
                            ChangeSet changeSet = null;
                            if (!String.IsNullOrEmpty(id))
                            {
                                changeSet = repository.GetChangeSet(id);
                                if (changeSet == null)
                                {
                                    string message = String.Format(CultureInfo.CurrentCulture, Resources.Error_DeploymentNotFound, id);
                                    result = NotFound(message);
                                    return;
                                }
                            }

                            try
                            {
                                await _deploymentManager.DeployAsync(repository, changeSet, username, clean, deploymentInfo: null, needFileUpdate: needFileUpdate);
                            }
                            catch (DeploymentFailedException ex)
                            {
                                if (!ArmUtils.IsArmRequest(Request))
                                {
                                    throw;
                                }

                                // if requests comes thru ARM, we adjust the error code from 500 -> 400
                                result = BadRequest(ex.ToString());
                                return;
                            }

                            // auto-swap
                            if (PostDeploymentHelper.IsAutoSwapEnabled())
                            {
                                if (changeSet == null)
                                {
                                    var targetBranch = _settings.GetBranch();
                                    changeSet = repository.GetChangeSet(targetBranch);
                                }

                                IDeploymentStatusFile statusFile = _status.Open(changeSet.Id);
                                if (statusFile != null && statusFile.Status == DeployStatus.Success)
                                {
                                    await PostDeploymentHelper.PerformAutoSwap(
                                        _environment.RequestId, 
                                        new PostDeploymentTraceListener(_tracer, _deploymentManager.GetLogger(changeSet.Id)));
                                }
                            }
                        }
                        catch (FileNotFoundException ex)
                        {
                            result = NotFound(ex);
                        }
                        catch (InvalidStatusException)
                        {
                            result = BadRequest("Only successful status can be active!");
                        }
                    }, "Performing deployment", TimeSpan.Zero);
                }
                catch (LockOperationException ex)
                {
                    return StatusCode(StatusCodes.Status409Conflict, ex.Message);
                }

                return result;
            }
        }

        public IActionResult CreateDeployment(DeployResult deployResult, string details)
        {
            var id = deployResult.Id;
            string path = Path.Combine(_environment.DeploymentsPath, id);
            IDeploymentStatusFile statusFile = _status.Open(id);
            if (statusFile != null)
            {
                return StatusCode(StatusCodes.Status409Conflict, String.Format("Deployment with id '{0}' exists", id));
            }

            FileSystemHelpers.EnsureDirectory(path);
            statusFile = _status.Create(id);
            statusFile.Status = deployResult.Status;
            statusFile.Message = deployResult.Message;
            statusFile.Deployer = deployResult.Deployer;
            statusFile.Author = deployResult.Author;
            statusFile.AuthorEmail = deployResult.AuthorEmail;
            statusFile.StartTime = deployResult.StartTime;
            statusFile.EndTime = deployResult.EndTime;

            // miscellaneous
            statusFile.Complete = true;
            statusFile.IsReadOnly = true;
            statusFile.IsTemporary = false;
            statusFile.ReceivedTime = deployResult.StartTime;
            // keep it simple regardless of success or failure
            statusFile.LastSuccessEndTime = deployResult.EndTime;
            statusFile.Save();

            if (deployResult.Current)
            {
                _status.ActiveDeploymentId = id;
            }

            var logger = new StructuredTextLogger(Path.Combine(path, DeploymentManager.TextLogFile), _analytics);
            ILogger innerLogger;
            if (deployResult.Status == DeployStatus.Success)
            {
                innerLogger = logger.Log("Deployment successful.");
            }
            else
            {
                innerLogger = logger.Log("Deployment failed.", LogEntryType.Error);
            }

            if (!String.IsNullOrEmpty(details))
            {
                innerLogger.Log(details);
            }

            return Ok();
        }

        public bool TryParseDeployResult(string id, JObject payload, out DeployResult deployResult)
        {
            deployResult = null;
            if (string.IsNullOrEmpty(id) || payload == null)
            {
                return false;
            }

            var status = payload.Value<int?>("status");
            if (status == null || (status.Value != 3 && status.Value != 4))
            {
                return false;
            }

            var message = payload.Value<string>("message");
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            var deployer = payload.Value<string>("deployer");
            if (string.IsNullOrEmpty(deployer))
            {
                return false;
            }

            var author = payload.Value<string>("author");
            if (string.IsNullOrEmpty(author))
            {
                return false;
            }

            deployResult = new DeployResult
            {
                Id = id,
                Status = (DeployStatus)status.Value,
                Message = message,
                Deployer = deployer,
                Author = author
            };

            // optionals
            var now = DateTime.UtcNow;
            deployResult.AuthorEmail = payload.Value<string>("author_email");
            deployResult.StartTime = payload.Value<DateTime?>("start_time") ?? now;
            deployResult.EndTime = payload.Value<DateTime?>("end_time") ?? now;

            // only success status can be active
            var active = payload.Value<bool?>("active");
            if (active == null)
            {
                deployResult.Current = deployResult.Status == DeployStatus.Success;
            }
            else
            {
                if (active.Value && deployResult.Status != DeployStatus.Success)
                {
                    throw new InvalidStatusException();
                }

                deployResult.Current = active.Value;
            }

            return true;
        }

        private class InvalidStatusException : Exception { }

        /// <summary>
        /// Get the list of all deployments
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public IActionResult GetDeployResults()
        {
            IActionResult result;
            EntityTagHeaderValue currentEtag = null;
            DeploymentsCacheItem cachedDeployments = _cachedDeployments;

            using (_tracer.Step("DeploymentService.GetCurrentEtag"))
            {
                currentEtag = GetCurrentEtag(Request);
                _tracer.Trace("Current Etag: {0}, Cached Etag: {1}", currentEtag, cachedDeployments.Etag);
            }

            // Avoid Caching when on K8
            if (EtagEquals(Request, currentEtag) && !PostDeploymentHelper.IsK8Environment())
            {
                result = StatusCode(StatusCodes.Status304NotModified);
            }
            else
            {
                using (_tracer.Step("DeploymentService.GetDeployResults"))
                {
                    if (!currentEtag.Equals(cachedDeployments.Etag))
                    {
                        cachedDeployments = new DeploymentsCacheItem
                        {
                            Results = GetResults(Request).ToList(),
                            Etag = currentEtag
                        };

                        _cachedDeployments = cachedDeployments;
                    }

                    result = Ok(ArmUtils.AddEnvelopeOnArmRequest(cachedDeployments.Results, Request));
                }
            }

            // CORE TODO Make sure this works properly
            // return etag
            //response.Headers.ETag = currentEtag;
            Response.GetTypedHeaders().ETag = currentEtag;

            return result;
        }

        /// <summary>
        /// Get the list of log entries for a deployment
        /// </summary>
        /// <param name="id">id of the deployment</param>
        /// <returns></returns>
        [HttpGet]
        public IActionResult GetLogEntry(string id)
        {
            using (_tracer.Step("DeploymentService.GetLogEntry"))
            {
                try
                {
                    var deployments = _deploymentManager.GetLogEntries(id).ToList();
                    foreach (var entry in deployments)
                    {
                        if (!entry.HasDetails) continue;
                        Uri baseUri = kUriHelper.MakeRelative(kUriHelper.GetBaseUri(Request), new Uri(Request.GetDisplayUrl()).AbsolutePath);
                        entry.DetailsUrl = kUriHelper.MakeRelative(baseUri, entry.Id);
                    }

                    return Ok(ArmUtils.AddEnvelopeOnArmRequest(deployments, Request));
                }
                catch (FileNotFoundException ex)
                {
                    return NotFound(ex);
                }
            }
        }

        /// <summary>
        /// Get the list of log entry details for a log entry
        /// </summary>
        /// <param name="id">id of the deployment</param>
        /// <param name="logId">id of the log entry</param>
        /// <returns></returns>
        [HttpGet]
        public IActionResult GetLogEntryDetails(string id, string logId)
        {
            using (_tracer.Step("DeploymentService.GetLogEntryDetails"))
            {
                try
                {
                    var details = _deploymentManager.GetLogEntryDetails(id, logId).ToList();
                    return details.Any()
                        ? (IActionResult)Ok(ArmUtils.AddEnvelopeOnArmRequest(details, Request))
                        : NotFound(String.Format(CultureInfo.CurrentCulture,
                        Resources.Error_LogDetailsNotFound,
                        logId,
                        id));
                }
                catch (FileNotFoundException ex)
                {
                    return NotFound(ex);
                }
            }
        }

        /// <summary>
        /// Get a deployment
        /// </summary>
        /// <param name="id">id of the deployment</param>
        /// <returns></returns>
        [HttpGet]
        public IActionResult GetResult(string id)
        {
            using (_tracer.Step("DeploymentService.GetResult"))
            {
                DeployResult pending;
                if (IsLatestPendingDeployment(ref id, out pending))
                {
                    Response.GetTypedHeaders().Location = new Uri(Request.GetDisplayUrl());
                    return Accepted(ArmUtils.AddEnvelopeOnArmRequest(pending, Request));
                }

                DeployResult result = _deploymentManager.GetResult(id);

                if (result == null)
                {
                    return NotFound(String.Format(CultureInfo.CurrentCulture,
                                                                       Resources.Error_DeploymentNotFound,
                                                                       id));
                }

                Uri baseUri = kUriHelper.MakeRelative(kUriHelper.GetBaseUri(Request), new Uri(Request.GetDisplayUrl()).AbsolutePath);
                result.Url = baseUri;
                result.LogUrl = kUriHelper.MakeRelative(baseUri, "log");

                return Ok(ArmUtils.AddEnvelopeOnArmRequest(result, Request));
            }
        }

        private bool IsLatestPendingDeployment(ref string id, out DeployResult pending)
        {
            if (String.Equals(Constants.LatestDeployment, id))
            {
                using (_tracer.Step("DeploymentService.GetLatestDeployment"))
                {
                    var results = _deploymentManager.GetResults();
                    pending = results.FirstOrDefault(r => r.Status != DeployStatus.Success && r.Status != DeployStatus.Failed);

                    if (pending != null)
                    {
                        _tracer.Trace("Deployment {0} is {1}", pending.Id, pending.Status);
                        return true;
                    }

                    var latest = results.Where(r => r.EndTime != null).OrderBy(r => r.EndTime.Value).LastOrDefault();
                    if (latest != null)
                    {
                        _tracer.Trace("Deployment {0} is {1} at {2}", latest.Id, latest.Status, latest.EndTime.Value.ToString("o"));

                        id = latest.Id;
                    }
                    else
                    {
                        _tracer.Trace("Could not find latest deployment!");
                    }
                }
            }

            pending = null;
            return false;
        }

        /// <summary>
        /// Gets a zip with containing deploy.cmd and .deployment
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public IActionResult GetDeploymentScript()
        {
            using (_tracer.Step("DeploymentService.GetDeploymentScript"))
            {
                if (!_deploymentManager.GetResults().Any())
                {
                    return NotFound("Need to deploy website to get deployment script.");
                }

                string deploymentScriptContent = _deploymentManager.GetDeploymentScriptContent();

                if (deploymentScriptContent == null)
                {
                    return NotFound("Operation only supported if not using a custom deployment script");
                }

                var result = new FileCallbackResult("application/zip", (outputStream, _) =>
                {
                    using (var zip = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: false))
                    {
                        // Add deploy.cmd to zip file
                        zip.AddFile(DeploymentManager.DeploymentScriptFileName, deploymentScriptContent);

                        // Add .deployment to cmd file
                        zip.AddFile(DeploymentSettingsProvider.DeployConfigFile, "[config]\ncommand = {0}\n".FormatInvariant(DeploymentManager.DeploymentScriptFileName));
                    }

                    return Task.CompletedTask;
                })
                {
                    FileDownloadName = "deploymentscript.zip"
                };

                return result;
            }
        }

        private EntityTagHeaderValue GetCurrentEtag(HttpRequest request)
        {
            return new EntityTagHeaderValue(String.Format("\"{0:x}\"", new Uri(request.GetDisplayUrl()).PathAndQuery.GetHashCode() ^ _status.LastModifiedTime.Ticks));
        }

        private static bool EtagEquals(HttpRequest request, EntityTagHeaderValue currentEtag)
        {
            // CORE TODO Double check this... before I found the GetTypedHeaders() extension method, I didn't think there was a typed implementation of the IfNoneMatch header
            // anymore, so I reimplemented based on the response caching middleware's implementation, leaving out the * match. See
            // https://github.com/aspnet/ResponseCaching/blob/52e8ffefb9b22c7c591aec15845baf07ed99356c/src/Microsoft.AspNetCore.ResponseCaching/ResponseCachingMiddleware.cs#L454-L478

            var ifNoneMatchHeader = request.Headers["If-None-Match"];

            if (StringValues.IsNullOrEmpty(ifNoneMatchHeader)) return false;
            return EntityTagHeaderValue.TryParseList(ifNoneMatchHeader, out IList<Microsoft.Net.Http.Headers.EntityTagHeaderValue> ifNoneMatchEtags) 
                   && ifNoneMatchEtags.Any(t => currentEtag.Compare(t, useStrongComparison: false));
        }

        private IEnumerable<DeployResult> GetResults(HttpRequest request)
        {
            foreach (var result in _deploymentManager.GetResults())
            {
                Uri baseUri = kUriHelper.MakeRelative(kUriHelper.GetBaseUri(request), new Uri(Request.GetDisplayUrl()).AbsolutePath);
                result.Url = kUriHelper.MakeRelative(baseUri, result.Id);
                result.LogUrl = kUriHelper.MakeRelative(baseUri, result.Id + "/log");
                yield return result;
            }
        }

        private JObject GetJsonContent()
        {
            try
            {
                JObject payload;
                using (var reader = new StreamReader(Request.Body))
                {
                    var jReader = new JsonTextReader(reader);
                    payload = JObject.Load(jReader);
                }
                
                if (ArmUtils.IsArmRequest(Request))
                {
                    payload = payload.Value<JObject>("properties");
                }

                return payload;
            }
            catch
            {
                // We're going to return null here since we don't want to force a breaking change
                // on the client side. If the incoming request isn't application/json, we want this
                // to return null.
                return null;
            }
        }

        class DeploymentsCacheItem
        {
            public static readonly DeploymentsCacheItem None = new DeploymentsCacheItem();

            public List<DeployResult> Results { get; set; }

            public EntityTagHeaderValue Etag { get; set; }
        }
    }
}
