using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;

namespace Kudu.Services.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
//            .UseKestrel(options => { options.Limits.MaxRequestBodySize = null; }) 
            .UseStartup<Startup>();
    }
}
