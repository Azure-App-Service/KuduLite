using System;
using System.IO;
using System.Net;
using System.Web;
using Kudu.Contracts.SourceControl;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.SourceControl;
using Kudu.Core.SourceControl.Git;
using Kudu.Core.Tracing;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

namespace Kudu.Services.GitServer
{
    /// <summary>
    /// This is a middleware class that processes custom git 
    /// repository requests
    /// </summary>
    /// <remarks>
    /// </remarks>
    public class CustomGitRepositoryHandlerMiddleware  
    {
        public enum GitServerRequestType
        {
            Unknown,
            AdvertiseUploadPack,
            AdvertiseReceivePack,
            ReceivePack,
            UploadPack,
            LegacyInfoRef,
        };

        private readonly Func<Type, object> _getInstance;
        private readonly ITracer _tracer;

        public CustomGitRepositoryHandlerMiddleware(RequestDelegate next)
        {
            // next is never used, this middleware is always terminal
        }

        public async Task Invoke(HttpContext context,
                                 Func<Type, object> getInstance)
        {
            using (_tracer.Step("CustomGitServerController.ProcessRequest"))
            {
                GitServerRequestType requestType;
                string repoRelFilePath;

                _tracer.Trace("Parsing request uri {0}", GetAbsoluteUri(context.Request));
                if (!TryParseUri(GetAbsoluteUri(context.Request), out repoRelFilePath, out requestType))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return;
                }

                IEnvironment env = GetInstance<IEnvironment>();
                var repoFullPath = Path.GetFullPath(Path.Combine(env.RootPath, repoRelFilePath));
                env.RepositoryPath = repoFullPath;
                _tracer.Trace("Using repository path: {0}", repoFullPath);

                switch (requestType)
                {
                    case GitServerRequestType.AdvertiseUploadPack:
                        using (_tracer.Step("CustomGitServerController.AdvertiseUploadPack"))
                        {
                            if (RepositoryExists(context))
                            {
                                var gitServer = GetInstance<IGitServer>();
                                context.Response.ContentType = "application/x-git-upload-pack-advertisement";
                                context.Response.StatusCode = (int)HttpStatusCode.OK;
                                // Helpers.PktWrite(context.Response,"# service==git-upload-pack\n");
                                // memoryStream.PktFlush();
                                using (var sw = new StreamWriter(context.Response.Body))
                                {
                                    sw.Write("# service=git-upload-pack\n");
                                    sw.Flush();
                                }
                                // context.Response.OutputStream.PktWrite("# service=git-upload-pack\n");
                                // context.Response.OutputStream.PktFlush();
                                context.Response.WriteNoCache();
                                gitServer.AdvertiseUploadPack(context.Response.Body);
                            }
                        }
                        break;
                    case GitServerRequestType.UploadPack:
                        if (RepositoryExists(context))
                        {
                            UploadPackHandlerMiddleware uploadPackHandler = GetInstance<UploadPackHandlerMiddleware>();
                            context.Response.StatusCode = (int)HttpStatusCode.OK;
                            await uploadPackHandler.Invoke(context,_tracer, GetInstance<IGitServer>());
                        }
                        break;
                    default:
                        context.Response.StatusCode = (int)HttpStatusCode.NotImplemented;
                        break;
                }
                return;
            }
        }

        // We need to take an instance constructor from so we can ensure we create the IGitServer after
        // repository path in IEnviroment is set.
        public CustomGitRepositoryHandlerMiddleware(Func<Type, object> getInstance)
        {
            _getInstance = getInstance;
            _tracer = GetInstance<ITracer>();
        }

        //  parse one of the four
        //  GET Git/{repositorypath}/info/refs?service=git-upload-pack
        //  GET Git/{repositorypath}/info/refs?service=git-receive-pack
        //  GET Git/{repositorypath}/info/refs
        //  POST Git/{repostiorypath}/git-receive-pack
        //  POST Git/{repostiorypath}/git-upload-pack
        public static bool TryParseUri(Uri url, out string repoRelLocalPath, out GitServerRequestType requestType)
        {
            repoRelLocalPath = null;
            requestType = GitServerRequestType.Unknown;
            // AbsolutePath returns encoded path, decoded so we can interpret as local file paths.
            var pathElts = HttpUtility.UrlDecode(url.AbsolutePath)
                                      .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (pathElts.Length < 2)
            {
                return false;
            }

            var repoPathEltStart = 1;
            var repoPathEltEnd = 0;
            var firstPathElt = pathElts[0];
            var lastPathElt = pathElts[pathElts.Length - 1];
            var nextToLastPathElt = pathElts[pathElts.Length - 2];

            if (!firstPathElt.Equals("git", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (nextToLastPathElt.Equals("info", StringComparison.OrdinalIgnoreCase) &&
               lastPathElt.Equals("refs", StringComparison.OrdinalIgnoreCase))
            {
                repoPathEltEnd = pathElts.Length - 2;
                var queryParams = HttpUtility.ParseQueryString(url.Query);
                var serviceValue = queryParams["service"];
                if (String.IsNullOrEmpty(url.Query))
                {
                    requestType = GitServerRequestType.LegacyInfoRef;
                }
                else if (serviceValue != null && serviceValue.Equals("git-upload-pack", StringComparison.OrdinalIgnoreCase))
                {
                    requestType = GitServerRequestType.AdvertiseUploadPack;
                }
                else if (serviceValue != null && serviceValue.Equals("git-receive-pack", StringComparison.OrdinalIgnoreCase))
                {
                    requestType = GitServerRequestType.AdvertiseReceivePack;
                }
                else
                {
                    return false;
                }
            }
            else if (lastPathElt.Equals("git-receive-pack", StringComparison.OrdinalIgnoreCase))
            {
                repoPathEltEnd = pathElts.Length - 1;
                requestType = GitServerRequestType.ReceivePack;
            }
            else if (lastPathElt.Equals("git-upload-pack", StringComparison.OrdinalIgnoreCase))
            {
                repoPathEltEnd = pathElts.Length - 1;
                requestType = GitServerRequestType.UploadPack;
            }
            else
            {
                return false;
            }

            var repoPathEltsCount = (repoPathEltEnd - repoPathEltStart);
            string[] repoPathElts = new string[repoPathEltsCount];
            Array.Copy(pathElts, repoPathEltStart, repoPathElts, 0, repoPathEltsCount);
            repoRelLocalPath = string.Join(@"\", repoPathElts);
            return true;
        }

        public bool RepositoryExists(HttpContext context)
        {
            // Ensure that the target directory has a git repository.
            IRepositoryFactory repositoryFactory = GetInstance<IRepositoryFactory>();
            IRepository repository = repositoryFactory.GetCustomRepository();
            if (repository != null && repository.RepositoryType == RepositoryType.Git)
            {
                return true;
            }
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return false;
        }

        public bool IsReusable
        {
            get { return false; }
        }

        private T GetInstance<T>()
        {
            return (T)_getInstance(typeof(T));
        }
        private static Uri GetAbsoluteUri(HttpRequest request)
        {
            UriBuilder uriBuilder = new UriBuilder();
            uriBuilder.Scheme = request.Scheme;
            uriBuilder.Host = request.Host.Host;
            uriBuilder.Path = request.Path.ToString();
            uriBuilder.Query = request.QueryString.ToString();
            return uriBuilder.Uri;
        }

    }
    public static class CustomGitRepositoryHandlerExtensions
    {
        public static IApplicationBuilder RunCustomGitRepositoryHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CustomGitRepositoryHandlerMiddleware>();
        }
    }
}
