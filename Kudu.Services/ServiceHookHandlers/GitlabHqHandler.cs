﻿using System;
using Newtonsoft.Json.Linq;
using Kudu.Contracts.SourceControl;
using Microsoft.AspNetCore.Http;

namespace Kudu.Services.ServiceHookHandlers
{
    public class GitlabHqHandler : GitHubCompatHandler
    {
        public GitlabHqHandler(IRepositoryFactory repositoryFactory)
            : base(repositoryFactory)
        {
        }

        //{
        //  "before": "2370e44c850e732d71edd2db36920482558e3fe0",
        //  "after": "2370e44c850e732d71edd2db36920482558e3fe0",
        //  "ref": "refs/heads/master",
        //  "checkout_sha": "2370e44c850e732d71edd2db36920482558e3fe0",
        //  "user_id": 99630,
        //  "user_name": "Suwat Bodin",
        //  "project_id": 171368,
        //  "repository": {
        //    "name": "HelloKudu",
        //    "url": "git@gitlab.com:KuduApps/HelloKudu.git",
        //    "description": "",
        //    "homepage": "https://gitlab.com/KuduApps/HelloKudu",
        //    "git_http_url": "https://gitlab.com/KuduApps/HelloKudu.git",
        //    "git_ssh_url": "git@gitlab.com:KuduApps/HelloKudu.git",
        //    "visibility_level": 20
        //  },
        //  "commits": [
        //    {
        //      "id": "2370e44c850e732d71edd2db36920482558e3fe0",
        //      "message": "Initial commit\n",
        //      "timestamp": "2012-02-14T10:45:08-08:00",
        //      "url": "https://gitlab.com/KuduApps/HelloKudu/commit/2370e44c850e732d71edd2db36920482558e3fe0",
        //      "author": {
        //        "name": "David Fowler",
        //        "email": "davidfowl@gmail.com"
        //      }
        //    }
        //  ],
        //  "total_commits_count": 1
        //}

        private const int PublicVisibilityLevel = 20;

        protected override bool ParserMatches(HttpRequest request, JObject payload, string targetBranch)
        {
            var repository = payload.Value<JObject>("repository");
            if (repository == null)
            {
                // doesn't look like GitlabHQ
                return false;
            }

            var userid = payload.Value<int?>("user_id");
            var username = payload.Value<string>("user_name");
            var url = repository.Value<string>("url");
            if (userid == null || username == null || url == null)
            {
                // doesn't look like GitlabHQ
                return false;
            }

            // The format of ref is refs/something/something else
            // For master it's normally refs/head/master
            string @ref = payload.Value<string>("ref");
            if (String.IsNullOrEmpty(@ref))
            {
                return false;
            }
            
            // FIXME deletecommit is considered as unknowpackage instead of noop
            // could be removed
            string newRef = payload.Value<string>("after");
            if (IsDeleteCommit(newRef))
            {
                return false;
            }

            return true;
        }

        protected override string DetermineSecurityProtocol(JObject payload)
        {
            JObject repository = payload.Value<JObject>("repository");
            var isPrivate = repository.Value<int>("visibility_level") != PublicVisibilityLevel;
            return isPrivate ? repository.Value<string>("git_ssh_url") : repository.Value<string>("git_http_url");
        }

        protected override string GetDeployer()
        {
            return "GitlabHQ";
        }
    }
}