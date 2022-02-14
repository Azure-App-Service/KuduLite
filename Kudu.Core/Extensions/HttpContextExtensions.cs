using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Kudu.Core.Extensions
{
    public static class HttpContextExtensions
    {
        public const string AppSettingsKey = "appSettings";
        public static IDictionary<string, string> GetAppSettings(this HttpContext httpContext)
        {
            return httpContext.GetItem<IDictionary<string, string>>(AppSettingsKey);
        }

        public static void SetAppSettings(this HttpContext httpContext, Func<IDictionary<string, string>> getAppSettingFunc)
        {
            if (httpContext.Items.ContainsKey(AppSettingsKey))
            {
                return;
            }

            httpContext.Items[AppSettingsKey] = new Lazy<IDictionary<string, string>>(getAppSettingFunc);
        }

        private static T GetItem<T>(this HttpContext httpcontext, string key)
        {
            if(httpcontext.Items.ContainsKey(key))
            {
                var value = httpcontext.Items[key];

                if (value is Lazy<T>)
                {
                    return (value as Lazy<T>).Value;
                }

                return (T)httpcontext.Items[key];
            }

            return default(T);
        }
    }
}
