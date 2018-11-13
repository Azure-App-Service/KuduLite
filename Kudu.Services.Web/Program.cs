using System.IO;
using System.Reflection;
using System.Xml;
using log4net;
using log4net.Config;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;

namespace Kudu.Services.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            
            /*
            XmlDocument log4netConfig = new XmlDocument();
            log4netConfig.Load(File.OpenRead("log4net.config"));
            var repo = log4net.LogManager.CreateRepository(Assembly.GetEntryAssembly(),
                typeof(log4net.Repository.Hierarchy.Hierarchy));
            log4net.Config.XmlConfigurator.Configure(repo, log4netConfig["log4net"]);
            */
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));
            CreateWebHostBuilder(args).Build().Run();
        }

        private static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseKestrel(options => { options.Limits.MaxRequestBodySize = null; })
                .UseStartup<Startup>();
    }
}