using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using Kudu.Contracts.Infrastructure;
using Kudu.Contracts.Tracing;
using Kudu.Core.SSHKey;
using Kudu.Core.Tracing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Internal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kudu.Services.SSHKey
{
    public class SSHKeyController : Controller
    {
        private const string KeyParameterName = "key";
        private const int LockTimeoutSecs = 5;

        private readonly ITracer _tracer;
        private readonly ISSHKeyManager _sshKeyManager;
        private readonly IOperationLock _sshKeyLock;

        public SSHKeyController(ITracer tracer, ISSHKeyManager sshKeyManager, IDictionary<string, IOperationLock> namedLocks)
        {
            _tracer = tracer;
            _sshKeyManager = sshKeyManager;
            _sshKeyLock = namedLocks["ssh"];
        }

        /// <summary>
        /// Set the private key. The supported key format is privacy enhanced mail (PEM)
        /// </summary>
        [HttpPut]
        [SuppressMessage("Microsoft.Usage", "CA2208:InstantiateArgumentExceptionsCorrectly", Justification = "By design")]
        public IActionResult SetPrivateKey([FromBody] string content)
        {
            string key;
            if (IsContentType("application/json"))
            {
                JObject result = GetJsonContent();
                key = result == null ? null : result.Value<string>(KeyParameterName);
            }
            else
            {
                // any other content-type assuming the content is key
                // curl http://server/sshkey -X PUT --upload-file /c/temp/id_rsa
                key = content;
            }

            if (String.IsNullOrEmpty(key))
            {
                return StatusCode(StatusCodes.Status400BadRequest, new ArgumentNullException(KeyParameterName));
                //throw new HttpResponseException(Request.CreateErrorResponse(HttpStatusCode.BadRequest, new ArgumentNullException(KeyParameterName)));
            }

            using (_tracer.Step("SSHKeyController.SetPrivateKey"))
            {
                IActionResult result = Ok();
                try
                {
                    _sshKeyLock.LockOperation(() =>
                    {
                        try
                        {
                            _sshKeyManager.SetPrivateKey(key);
                        }
                        catch (ArgumentException ex)
                        {
                            result = StatusCode(StatusCodes.Status400BadRequest, ex.Message);
                        }
                        catch (InvalidOperationException ex)
                        {
                            result = StatusCode(StatusCodes.Status409Conflict, ex.Message);
                        }
                    }, "Updating SSH key", TimeSpan.FromSeconds(LockTimeoutSecs));
                }
                catch (LockOperationException ex)
                {
                    result = StatusCode(StatusCodes.Status409Conflict, ex.Message);
                }
                return result;
            }
        }

        [HttpGet]
        public IActionResult GetPublicKey(string ensurePublicKey = null)
        {
            bool ensurePublicKeyValue = StringUtils.IsTrueLike(ensurePublicKey);

            using (_tracer.Step("SSHKeyController.GetPublicKey"))
            {
                IActionResult result = Ok();
                try
                {
                    _sshKeyLock.LockOperation(() =>
                    {
                        try
                        {
                            result = Json(_sshKeyManager.GetPublicKey(ensurePublicKeyValue)?? string.Empty);
                        }
                        catch (InvalidOperationException ex)
                        {
                            result = StatusCode(StatusCodes.Status409Conflict, ex.Message);
                        }
                    }, "Getting SSH key", TimeSpan.FromSeconds(LockTimeoutSecs));
                }
                catch (LockOperationException ex)
                {
                    result =  StatusCode(StatusCodes.Status409Conflict, ex.Message);
                }
                return result;
            }
        }

        [HttpDelete]
        public IActionResult DeleteKeyPair()
        {
            using (_tracer.Step("SSHKeyController.GetPublicKey"))
            {
                IActionResult result = Ok();
                try
                {
                    _sshKeyLock.LockOperation(() =>
                    {
                        try
                        {
                            _sshKeyManager.DeleteKeyPair();
                        }
                        catch (InvalidOperationException ex)
                        {
                            result = StatusCode(StatusCodes.Status409Conflict, ex.Message);
                        }
                    }, "Deleting SSH Key", TimeSpan.FromSeconds(LockTimeoutSecs));
                }
                catch (LockOperationException ex)
                {
                    result = StatusCode(StatusCodes.Status409Conflict, ex.Message);
                }
                return result;
            }
        }

        private bool IsContentType(string mediaType)
        {            
            return Request.Headers.ContainsKey("MediaType")
                   && (Request.Headers["MediaType"].ToString() ?? "")
                   .StartsWith(mediaType, StringComparison.OrdinalIgnoreCase);
            
            //return contentType.MediaType != null &&
            //   contentType.MediaType.StartsWith(mediaType, StringComparison.OrdinalIgnoreCase);
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
    }
}