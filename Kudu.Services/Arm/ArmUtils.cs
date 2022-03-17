using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using Kudu.Contracts.Infrastructure;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace Kudu.Services.Arm
{
    public static class ArmUtils
    {
        public const string GeoLocationHeaderKey = "x-ms-geo-location";

        public static object AddEnvelopeOnArmRequest<T>(T namedObject, HttpRequest request, Uri originalUri = null) where T : INamedObject
        {
            if (IsArmRequest(request))
            {
                return Create(namedObject, request, originalUri);
            }

            return namedObject;
        }

        public static object AddEnvelopeOnArmRequest<T>(List<T> namedObjects, HttpRequest request) where T : INamedObject
        {
            return AddEnvelopeOnArmRequest((IEnumerable<T>)namedObjects, request);
        }

        public static object AddEnvelopeOnArmRequest<T>(IEnumerable<T> namedObjects, HttpRequest request) where T : INamedObject
        {
            if (IsArmRequest(request))
            {
                return Create(namedObjects, request);
            }

            return namedObjects;
        }

        public static bool IsArmRequest(HttpRequest request)
        {
            return request != null &&
                   request.Headers != null &&
                   request.Headers.ContainsKey(GeoLocationHeaderKey);
        }

        private static ArmListEntry<T> Create<T>(IEnumerable<T> objects, HttpRequest request) where T : INamedObject
        {
            return new ArmListEntry<T>
            {
                Value = objects.Select(entry => Create(entry, request, null, isChild: true))
            };
        }

        private static ArmEntry<T> Create<T>(T o, HttpRequest request, Uri originalUri, bool isChild = false) where T : INamedObject
        {
            var armEntry = new ArmEntry<T>()
            {
                Properties = o
            };

            armEntry.Id = originalUri?.AbsolutePath ?? GetOriginalUri(request).AbsolutePath;

            // If we're generating a child object, append the child name
            if (isChild)
            {
                if (!armEntry.Id.EndsWith("/", StringComparison.OrdinalIgnoreCase))
                {
                    armEntry.Id += '/';
                }

                armEntry.Id += ((INamedObject)o).Name;
            }

            armEntry.Id = armEntry.Id.TrimEnd('/');

            // The Type and Name properties use alternating token starting with 'Microsoft.Web/sites'
            // e.g. /subscriptions/b0019e1d-2829-4226-9356-4a57a4a5cc90/resourcegroups/MyRG/providers/Microsoft.Web/sites/MySite/extensions/SettingsAPISample/settings/foo1
            // Type: Microsoft.Web/sites/extensions/settings
            // Name: MySite/SettingsAPISample/foo1

            string[] idTokens = armEntry.Id.Split('/');
            if (idTokens.Length > 8 && idTokens[6] == "Microsoft.Web")
            {
                armEntry.Type = idTokens[6];

                for (int i = 7; i < idTokens.Length; i += 2)
                {
                    armEntry.Type += "/" + idTokens[i];
                }

                armEntry.Name = idTokens[8];
                for (int i = 10; i < idTokens.Length; i += 2)
                {
                    armEntry.Name += "/" + idTokens[i];
                }
            }

            //IEnumerable<string> values;
            //if (request.Headers.TryGetValues(GeoLocationHeaderKey, out values))
            //{
            //    armEntry.Location = values.FirstOrDefault();
            //}

            if (request.Headers.ContainsKey(GeoLocationHeaderKey))
            {
                armEntry.Location = request.Headers[GeoLocationHeaderKey].First();
            }

            return armEntry;
        }

        public static Uri GetOriginalUri(HttpRequest request)
        {
            if (IsArmRequest(request))
            {
                var referrer = request.Headers["Referer"].ToString(); // NOT MISSPELLED, https://en.wikipedia.org/wiki/HTTP_referer
                return !string.IsNullOrEmpty(referrer) ? new Uri(referrer) : new Uri(request.GetDisplayUrl());
            }
            else
            {
                return new Uri(request.GetDisplayUrl());
            }
        }

        // CORE TODO
        /*
        public static HttpResponseM CreateErrorResponse(HttpRequest request, HttpStatusCode statusCode, Exception exception)
        {
            if (IsArmRequest(request))
            {
                return request.CreateResponse(statusCode, new ArmErrorInfo(statusCode, exception));
            }

            return request.CreateErrorResponse(statusCode, exception);
        }
        */

        // this error will be deserialized conforming with ARM spec
        public class ArmErrorInfo
        {
            public ArmErrorInfo(HttpStatusCode code, Exception exception)
            {
                Error = new ArmErrorDetails
                {
                    Code = code.ToString(),
                    Message = exception.ToString()
                };
            }

            [JsonProperty(PropertyName = "error")]
            public ArmErrorDetails Error { get; private set; }

            public class ArmErrorDetails
            {
                [JsonProperty(PropertyName = "code")]
                public string Code { get; set; }

                [JsonProperty(PropertyName = "message")]
                public string Message { get; set; }
            }
        }
    }
}
