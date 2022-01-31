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
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.SourceControl;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Deployment;
using Kudu.Core.Helpers;
using Kudu.Core.SourceControl;
using Kudu.Core.SourceControl.Git;
using Kudu.Core.Tracing;
using Kudu.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kudu.Core.K8SE;

namespace Kudu.Services.GitServer
{
    public class ReceivePackHandlerMiddleware
    {
        public ReceivePackHandlerMiddleware(RequestDelegate next)
        {
            // next is never used, this middleware is always terminal
        }

        public async Task Invoke(
            HttpContext context,
            ITracer tracer,
            IGitServer gitServer,
            IDictionary<string, IOperationLock> namedLocks,
            IDeploymentManager deploymentManager,
            IRepositoryFactory repositoryFactory,
            IEnvironment environment)
        {
            //Get the deployment lock from the locks dictionary
            var deploymentLock = namedLocks[Constants.DeploymentLockName];

            using (tracer.Step("RpcService.ReceivePack"))
            {
                // Ensure that the target directory does not have a non-Git repository.
                IRepository repository = repositoryFactory.GetRepository();
                if (repository != null && repository.RepositoryType != RepositoryType.Git)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                try
                {
                    await deploymentLock.LockOperationAsync(() =>
                    {
                        context.Response.ContentType = "application/x-git-receive-pack-result";

                        if (PostDeploymentHelper.IsAutoSwapOngoing())
                        {
                            context.Response.StatusCode = StatusCodes.Status409Conflict;
                            var msg = Encoding.UTF8.GetBytes(Resources.Error_AutoSwapDeploymentOngoing);
                            return context.Response.Body.WriteAsync(msg, 0, msg.Length);
                        }

                        string username = null;
                        if (AuthUtility.TryExtractBasicAuthUser(context.Request, out username))
                        {
                            gitServer.SetDeployer(username);
                        }

                        UpdateNoCacheForResponse(context.Response);

                        // This temporary deployment is for ui purposes only, it will always be deleted via finally.
                        ChangeSet tempChangeSet;
                        using (deploymentManager.CreateTemporaryDeployment(Resources.ReceivingChanges, out tempChangeSet))
                        {
                            var env = GetEnvironment(context, environment);
                            var requestIdEnv = string.Format(Constants.RequestIdEnvFormat, env.K8SEAppName);

                            // to pass to kudu.exe post receive hook
                            System.Environment.SetEnvironmentVariable(requestIdEnv, env.RequestId);

                            try
                            {
                                gitServer.Receive(context.Request.Body, context.Response.Body);
                            }
                            finally
                            {
                                System.Environment.SetEnvironmentVariable(requestIdEnv, null);
                            }
                        }

                        return Task.CompletedTask;
                    }, "Handling git receive pack", TimeSpan.Zero);
                }
                catch (LockOperationException ex)
                {
                    context.Response.StatusCode = StatusCodes.Status409Conflict;
                    var msg = Encoding.UTF8.GetBytes(ex.Message);
                    await context.Response.Body.WriteAsync(msg, 0, msg.Length);
                }
            }
        }

        // CORE TODO pulled from GitServerHttpHandler and duplicated in both handlers
        public static void UpdateNoCacheForResponse(HttpResponse response)
        {
            // CORE TODO no longer exists
            //response.Buffer = false;
            //response.BufferOutput = false;

            response.Headers["Expires"] = "Fri, 01 Jan 1980 00:00:00 GMT";
            response.Headers["Pragma"] = "no-cache";
            response.Headers["Cache-Control"] = "no-cache, max-age=0, must-revalidate";
        }


        private IEnvironment GetEnvironment(HttpContext context, IEnvironment environment)
        {
            if (!K8SEDeploymentHelper.IsK8SEEnvironment())
            {
                return environment;
            }
            else
            {
                return (IEnvironment)context.Items["environment"];
            }
        }
    }

    public static class ReceivePackHandlerExtensions
    {
        public static IApplicationBuilder RunReceivePackHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ReceivePackHandlerMiddleware>();
        }
    }
}