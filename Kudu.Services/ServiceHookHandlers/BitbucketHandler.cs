﻿using Kudu.Contracts.SourceControl;
using Kudu.Core.Deployment;
using Kudu.Core.SourceControl;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace Kudu.Services.ServiceHookHandlers
{
    public class BitbucketHandler : ServiceHookHandlerBase
    {
        public BitbucketHandler(IRepositoryFactory repositoryFactory)
            : base(repositoryFactory)
        {
        }

        public override DeployAction TryParseDeploymentInfo(HttpRequest request, JObject payload, string targetBranch, out DeploymentInfoBase deploymentInfo)
        {
            deploymentInfo = null;
            
            if (request.Headers["User-Agent"].ToString().StartsWith("Bitbucket.org", StringComparison.OrdinalIgnoreCase))
            {
                deploymentInfo = GetDeploymentInfo(payload, targetBranch);
                return deploymentInfo == null ? DeployAction.NoOp : DeployAction.ProcessDeployment;
            }

            return DeployAction.UnknownPayload;
        }

        protected DeploymentInfoBase GetDeploymentInfo(JObject payload, string targetBranch)
        {
            // bitbucket format
            // { repository: { absolute_url: "/a/b", is_private: true }, canon_url: "https//..." } 
            var repository = payload.Value<JObject>("repository");

            var info = new DeploymentInfo(RepositoryFactory)
            {
                Deployer = "Bitbucket",
                IsContinuous = true
            };

            // Per bug #623, Bitbucket sends an empty commits array when a commit has a large number of files. 
            // In this case, we'll attempt to fetch regardless.
            var commits = payload.Value<JArray>("commits");
            if (commits != null && commits.Count > 0)
            {
                // Identify the last commit for the target branch. 
                JObject targetCommit = (from commit in commits
                                        where targetBranch.Equals(commit.Value<string>("branch") ?? targetBranch, StringComparison.OrdinalIgnoreCase)
                                        orderby TryParseCommitStamp(commit.Value<string>("utctimestamp")) descending
                                        select (JObject)commit).FirstOrDefault();


                if (targetCommit == null)
                {
                    return null;
                }
                info.TargetChangeset = new ChangeSet(
                    id: targetCommit.Value<string>("raw_node"),
                    authorName: targetCommit.Value<string>("author"),  // The Bitbucket id for the user.
                    authorEmail: null,                                 // TODO: Bitbucket gives us the raw_author field which is the user field set in the repository, maybe we should parse it.
                    message: (targetCommit.Value<string>("message") ?? String.Empty).TrimEnd(),
                    timestamp: TryParseCommitStamp(targetCommit.Value<string>("utctimestamp"))
                );
            }
            else
            {
                info.TargetChangeset = new ChangeSet(id: String.Empty, authorName: null,
                                        authorEmail: null, message: null, timestamp: DateTime.UtcNow);
            }

            string server = payload.Value<string>("canon_url");     // e.g. https://bitbucket.org
            string path = repository.Value<string>("absolute_url"); // e.g. /davidebbo/testrepo/
            string scm = repository.Value<string>("scm"); // e.g. hg
            bool isPrivate = repository.Value<bool>("is_private");

            // Combine them to get the full URL
            info.RepositoryUrl = server + path;
            info.RepositoryType = scm.Equals("hg", StringComparison.OrdinalIgnoreCase) ? RepositoryType.Mercurial : RepositoryType.Git;

            // private repo, use SSH
            if (isPrivate)
            {
                info.RepositoryUrl = GetPrivateRepoUrl(info.RepositoryUrl, info.RepositoryType);
            }

            return info;
        }

        internal static DateTimeOffset TryParseCommitStamp(string value)
        {
            DateTimeOffset dateTime;
            return DateTimeOffset.TryParse(value, out dateTime) ? dateTime : DateTimeOffset.MinValue;
        }

        internal static string GetPrivateRepoUrl(string publicRepoUrl, RepositoryType repoType)
        {
            var uri = new UriBuilder(publicRepoUrl);
            if (uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                uri.Scheme = "ssh";
                uri.Port = -1;
                uri.Host = (repoType == RepositoryType.Mercurial ? "hg@" : "git@") + uri.Host;

                // Private repo paths are of the format ssh://git@bitbucket.org/accountname/reponame.git
                return uri.ToString();
            }

            return publicRepoUrl;
        }
    }
}