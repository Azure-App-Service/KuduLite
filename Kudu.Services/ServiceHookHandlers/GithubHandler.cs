﻿using Kudu.Contracts.SourceControl;
using Kudu.Core;
using Kudu.Core.Infrastructure;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace Kudu.Services.ServiceHookHandlers
{
    public class GitHubHandler : GitHubCompatHandler
    {
        private IEnvironment _environment;
        private const string PrivateKeyFile = "id_rsa";

        public GitHubHandler()
            : base(null)
        {
            // do nothing, 5 tests called this function
            _environment = null;
        }

        public GitHubHandler(IEnvironment enviromentInject, IRepositoryFactory repositoryFactory)
            : base(repositoryFactory)
        {
            _environment = enviromentInject;
        }

        protected override bool ParserMatches(HttpRequest request, JObject payload, string targetBranch)
        {
            return request.Headers.ContainsKey("X-Github-Event");
        }

        protected override bool IsNoop(HttpRequest request, JObject payload, string targetBranch)
        {
            // FIXME if githubcompathandler failed to parse the body => NOOP
            return !(base.ParserMatches(request, payload, targetBranch)) || base.IsNoop(request, payload, targetBranch);
        }

        protected override string DetermineSecurityProtocol(JObject payload)
        {
            JObject repository = payload.Value<JObject>("repository");
            string repositoryUrl = repository.Value<string>("url");
            bool isPrivate = repository.Value<bool>("private");
            if (isPrivate)
            {
                repositoryUrl = repository.Value<string>("ssh_url");
            }
            else if (!String.Equals(new Uri(repositoryUrl).Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                
                if (_environment != null && FileSystemHelpers.FileExists(Path.Combine(_environment.SSHKeyPath, PrivateKeyFile)))
                {
                    // if we determine that a github request does not come from "github.com"
                    // and it is not a private repository, but we still find a ssh key, we assume 
                    // the request is from a private GHE 
                    repositoryUrl = repository.Value<string>("ssh_url");
                }
            }
            return repositoryUrl;
        }

        protected override string GetDeployer()
        {
            return "GitHub";
        }
    }
}