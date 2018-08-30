using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;

namespace Kudu.Services.Web
{
    /// <summary>
    /// <see cref="https://docs.microsoft.com/en-us/aspnet/core/mvc/controllers/filters#Dependency injection"/>
    /// </summary>
    public class AutoLogAttribute : TypeFilterAttribute
    {
        public AutoLogAttribute() : base(typeof(AutoLogActionFilterImpl))
        {

        }

        private class AutoLogActionFilterImpl : IActionFilter
        {
            public AutoLogActionFilterImpl(HttpContext context)
            {
                Console.WriteLine($"path: {context.Request.Path}");
            }
            public void OnActionExecuting(ActionExecutingContext context)
            {
                // perform some business logic work
                Console.WriteLine("Hello World!");
            }

            public void OnActionExecuted(ActionExecutedContext context)
            {
                //TODO: log body content and response as well
                Console.WriteLine($"path: {context.HttpContext.Request.Path}");
            }
        }
    }
}
