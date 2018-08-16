using System;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;

namespace Kudu.Services.Web.Infrastructure
{

    // CORE NOTE This was renamed/reworked from RouteCollectionExtensions
    public static class RouteBuilderExtensions
    {
        public const string DeprecatedKey = "deprecated";

        public static void MapHttpWebJobsRoute(this IRouteBuilder routes, string name, string jobType, string routeTemplate, object defaults, object constraints = null)
        {
            // e.g. api/continuouswebjobs/foo
            routes.MapHttpRoute(name, String.Format("api/{0}webjobs{1}", jobType, routeTemplate), defaults, constraints, deprecated: false);

            // e.g. api/triggeredjobs/foo/history/17
            routes.MapHttpRoute(name + "-dep", String.Format("api/{0}jobs{1}", jobType, routeTemplate), defaults, constraints, deprecated: true);

            // e.g. jobs/triggered/foo/history/17 and api/jobs/triggered/foo/history/17
            routes.MapHttpRouteDual(name + "-old", String.Format("jobs/{0}{1}", jobType, routeTemplate), defaults, constraints, bothDeprecated: true);
        }

        public static void MapHttpProcessesRoute(this IRouteBuilder routes, string name, string routeTemplate, object defaults, object constraints = null)
        {
            // e.g. api/processes/3958/dump
            routes.MapHttpRoute(name + "-direct", "api/processes" + routeTemplate, defaults, constraints, deprecated: false);

            // e.g. api/diagnostics/processes/4845
            routes.MapHttpRouteDual(name, "diagnostics/processes" + routeTemplate, defaults, constraints);
        }

        public static void MapHttpRouteDual(this IRouteBuilder routes, string name, string routeTemplate, object defaults, object constraints = null, bool bothDeprecated = false)
        {
            routes.MapHttpRoute(name + "-dep", routeTemplate, defaults, constraints, deprecated: true);
            routes.MapHttpRoute(name, "api/" + routeTemplate, defaults, constraints, deprecated: bothDeprecated);
        }

        public static void MapHttpRoute(this IRouteBuilder routes, string name, string routeTemplate, object defaults, object constraints, bool deprecated)
        {
            // CORE TODO Note that the only place that the deprecated datatoken is used, it only checks to see
            // if the key is *there*, not the bool value of it, so this is a little awkward looking due to the way
            // anonymous objects work.
            if (deprecated)
            {
                name += "-dep";
                routes.MapRoute(name, routeTemplate, defaults, constraints, new { DeprecatedKey = true });
            }
            else
            {
                routes.MapRoute(name, routeTemplate, defaults, constraints);
            }
        }

        // CORE TODO Commenting these out for now. There's a CORE TODO in Startup.cs about supporting the concept of "deprecated" routes.

        /*
        public static void MapHandlerDual<THandler>(this IRouteBuilder routes, IKernel kernel, string name, string url)
        {
            routes.MapHandler<THandler>(kernel, name + "-old", url, deprecated: true);
            routes.MapHandler<THandler>(kernel, name, "api/" + url, deprecated: false);
        }

        public static void MapHandler<THandler>(this IRouteBuilder routes, IKernel kernel, string name, string url, bool deprecated)
        {
            var route = new Route(url, new HttpHandlerRouteHandler(kernel, typeof(THandler)));
            routes.Add(name, route);
            if (deprecated)
            {
                DeprecateRoute(route);
            }
        }
        */
    }
}
