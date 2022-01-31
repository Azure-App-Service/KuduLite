#region License

// Copyright 2010 Jeremy Skinner (http://www.jeremyskinner.co.uk)
//  
// Licensed under the Apache License, Version 2.0 (the "License"); 
// you may not use this file except in compliance with the License. 
// You may obtain a copy of the License at 
// 
// http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software 
// distributed under the License is distributed on an "AS IS" BASIS, 
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and 
// limitations under the License.
// 
// The latest version of this file can be found at http://github.com/JeremySkinner/git-dot-aspx

// This file was modified from the one found in git-dot-aspx

#endregion
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Net.Http.Headers;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Settings;
using Kudu.Contracts.SourceControl;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Deployment;
using Kudu.Core.SourceControl;
using Kudu.Core.SourceControl.Git;
using Kudu.Core.Tracing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kudu.Services.GitServer
{
    /// <summary>
    /// 
    /// </summary>
    public class InfoRefsController : Controller
    {
        private readonly ITracer _tracer;
        private readonly IRepositoryFactory _repositoryFactory;
        private readonly Func<Type, object> _getInstance;

        // Delay ninject binding
        // CORE TODO "Delayed binding" here is just the service locator [anti]pattern,
        // might be good to refactor this. I don't see a reason for it. Note that there's
        // no longer any setup for InfoRefsController in Startup.cs because IServiceProvider
        // gets injected automatically.
        public InfoRefsController(IServiceProvider serviceProvider)
        {
            _getInstance = t => serviceProvider.GetRequiredService(t);
            _repositoryFactory = GetInstance<IRepositoryFactory>();
            _tracer = GetInstance<ITracer>();
        }

        /// <summary>
        /// Returns the result of Execute action of info/refs controller
        /// Responds to the relative URL : ?svc=git&q=/info/refs/execute&service=git-recieve-pack
        /// </summary>
        /// <param name="service">
        /// The query string variable "service" having values such as 
        /// git-recieve-pack/git-upload-pack
        /// </param>
        [HttpGet]
        public IActionResult Execute(string service = null)
        {
            using (_tracer.Step("InfoRefsService.Execute"))
            {
                // Ensure that the target directory does not have a non-Git repository.
                IRepository repository = _repositoryFactory.GetRepository();
                if (repository != null && repository.RepositoryType != RepositoryType.Git)
                {
                    return BadRequest(String.Format(CultureInfo.CurrentCulture, Resources.Error_NonGitRepositoryFound, repository.RepositoryType));
                }

                service = GetServiceType(service);
                bool isUsingSmartProtocol = service != null;

                // Service has been specified - we're working with the smart protocol
                if (isUsingSmartProtocol)
                {
                    return SmartInfoRefs(service);
                }

                // Dumb protocol isn't supported
                _tracer.TraceWarning("Attempting to use dumb protocol.");
                return StatusCode(StatusCodes.Status501NotImplemented, Resources.Error_DumbProtocolNotSupported);
            }
        }

        /// <summary>
        /// Returns the result of SmartInfoRefs action of info/refs controller
        /// Responds to the relative URL : ?svc=git&q=/info/refs/SmartInfoRefs&service=git-recieve-pack
        /// </summary>
        /// <param name="service">
        /// The query string variable "service" having values such as 
        /// git-recieve-pack/git-upload-pack
        /// </param>
        private IActionResult SmartInfoRefs(string service)
        {
            using (_tracer.Step("InfoRefsService.SmartInfoRefs"))
            {
                // Will be disposed by MVC after the FileStreamResult is processed
                var memoryStream = new MemoryStream();

                memoryStream.PktWrite("# service=git-{0}\n", service);
                memoryStream.PktFlush();

                if (service == "upload-pack")
                {
                    InitialCommitIfNecessary();

                    var gitServer = GetInstance<IGitServer>();
                    gitServer.AdvertiseUploadPack(memoryStream);
                }
                else if (service == "receive-pack")
                {
                    var gitServer = GetInstance<IGitServer>();
                    gitServer.AdvertiseReceivePack(memoryStream);
                }

                _tracer.Trace("Writing {0} bytes", memoryStream.Length);

                var headers = Response.GetTypedHeaders();

                // Explicitly set the charset to empty string
                // We do this as certain git clients (jgit) require it to be empty.
                // If we don't set it, then it defaults to utf-8, which breaks jgit's logic for detecting smart http
                var contentType = new MediaTypeHeaderValue("application/x-git-{0}-advertisement".With(service))
                {
                    Charset = ""
                };

                Response.WriteNoCache();
                memoryStream.Seek(0, SeekOrigin.Begin);
                return new FileStreamResult(memoryStream, contentType);
            }
        }
        /// <summary>
        /// Checks for null string and removes the prefix "git-" to get the 
        /// type of Git Http Service i.e upload-pack/recieve-pack.  
        /// </summary>
        /// <param name="service">
        /// The query string value for variable "service"
        /// </param>
        protected static string GetServiceType(string service)
        {
            if (string.IsNullOrWhiteSpace(service))
            {
                return null;
            }

            return service.Replace("git-", "");
        }

        private T GetInstance<T>()
        {
            return (T)_getInstance(typeof(T));
        }

        /// <summary>
        /// 
        /// </summary>
        public void InitialCommitIfNecessary()
        {
            var settings = GetInstance<IDeploymentSettingsManager>();

            // only support if LocalGit
            if (settings.GetValue(SettingsKeys.ScmType) != ScmType.LocalGit)
            {
                return;
            }

            // get repository for the WebRoot
            var initLock = GetInstance<IDictionary<string,IOperationLock>>();
            initLock[Constants.DeploymentLockName].LockOperation(() =>
            {
                IRepository repository = _repositoryFactory.GetRepository();

                // if repository exists, no need to do anything
                if (repository != null)
                {
                    return;
                }

                var repositoryPath = settings.GetValue(SettingsKeys.RepositoryPath);

                // if repository settings is defined, it's already been taken care.
                if (!String.IsNullOrEmpty(repositoryPath))
                {
                    return;
                }

                var env = GetInstance<IEnvironment>();
                // it is default webroot content, do nothing
                if (DeploymentHelper.IsDefaultWebRootContent(env.WebRootPath))
                {
                    return;
                }

                // Set repo path to WebRoot
                var previous = env.RepositoryPath;
                env.RepositoryPath = Path.Combine(env.SiteRootPath, Constants.WebRoot);

                repository = _repositoryFactory.GetRepository();
                if (repository != null)
                {
                    env.RepositoryPath = previous;
                    return;
                }

                // do initial commit
                repository = _repositoryFactory.EnsureRepository(RepositoryType.Git);

                // Once repo is init, persist the new repo path
                settings.SetValue(SettingsKeys.RepositoryPath, Constants.WebRoot);

                repository.Commit("Initial Commit", authorName: null, emailAddress: null);

            }, "Cloning repository", GitExeServer.InitTimeout);
        }
    }
}
