﻿using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace Kudu.Services.Util
{
    /// <summary>
    /// Filter to set size limits for request form data
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class RequestFormSizeLimitAttribute : Attribute, IAuthorizationFilter, IOrderedFilter
    {
        private readonly FormOptions _formOptions;

        public RequestFormSizeLimitAttribute(int valueCountLimit)
        {
            _formOptions = new FormOptions()
            {
                ValueCountLimit = valueCountLimit
            };
        }

        public int Order { get; set; }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var features = context.HttpContext.Features;
            var formFeature = features.Get<IFormFeature>();

            if (formFeature == null || formFeature.Form == null)
            {
                // Request form has not been read yet, so set the limits
                features.Set<IFormFeature>(new FormFeature(context.HttpContext.Request, _formOptions));
            }
        }
    }
}
