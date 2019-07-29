using Microsoft.AspNetCore.StaticFiles;
using System;
using System.Collections.Concurrent;
using Microsoft.Net.Http.Headers;

namespace Kudu.Services.Infrastructure
{
    /// <summary>
    /// Provides a cache of file name extensions to media type mappings
    /// </summary>
    public class MediaTypeMap
    {
        private static readonly MediaTypeMap _defaultInstance = new MediaTypeMap();
        private static readonly FileExtensionContentTypeProvider _typeProvider = new FileExtensionContentTypeProvider();
        private readonly ConcurrentDictionary<string, MediaTypeHeaderValue> _mediatypeMap = CreateMediaTypeMap();
        private readonly MediaTypeHeaderValue _defaultMediaType = MediaTypeHeaderValue.Parse("application/octet-stream");

        public static MediaTypeMap Default
        {
            get { return _defaultInstance; }
        }

        // CORE TODO Double check this. We no longer have MimeMapping so I use FileExtensionContentTypeProvider
        // from the Microsoft.AspNetCore.StaticFiles package. I left in the ConcurrentDictionary usage and the
        // prepopulation of a couple of types (js, json, md) even though FECTP seems to already have them,
        // but I don't think the complexity of it is really needed.
        public MediaTypeHeaderValue GetMediaType(string fileExtension)
        {
            if (fileExtension == null)
            {
                throw new ArgumentNullException("fileExtension");
            }

            return _mediatypeMap.GetOrAdd(fileExtension,
                (extension) =>
                {
                    try
                    {
                        _typeProvider.TryGetContentType(fileExtension, out string mediaTypeValue);
                        MediaTypeHeaderValue mediaType;
                        if (mediaTypeValue != null && MediaTypeHeaderValue.TryParse(mediaTypeValue, out mediaType))
                        {
                            return mediaType;
                        }
                        return _defaultMediaType;
                    }
                    catch
                    {
                        return _defaultMediaType;
                    }
                });
        }

        private static ConcurrentDictionary<string, MediaTypeHeaderValue> CreateMediaTypeMap()
        {
            var dictionary = new ConcurrentDictionary<string, MediaTypeHeaderValue>(StringComparer.OrdinalIgnoreCase);
            dictionary.TryAdd(".js", MediaTypeHeaderValue.Parse("application/javascript"));
            dictionary.TryAdd(".json", MediaTypeHeaderValue.Parse("application/json"));
            dictionary.TryAdd(".log", MediaTypeHeaderValue.Parse("text/plain"));

            // Add media type for markdown
            dictionary.TryAdd(".md", MediaTypeHeaderValue.Parse("text/plain"));

            return dictionary;
        }
    }
}
