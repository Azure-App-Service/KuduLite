using System.IO;
using System.Reflection;
using Kudu.Contracts.Settings;
using Kudu.Core.Helpers;
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
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));
            InitializeProcess();
            CreateWebHostBuilder(args).Build().Run();
        }

        private static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseKestrel(options => { options.Limits.MaxRequestBodySize = null; })
                .UseStartup<Startup>();

        /// <summary>
        /// Perform any process level initialization that needs to happen BEFORE
        /// the WebHost is initialized.
        /// </summary>
        private static void InitializeProcess()
        {
            if (!OSDetector.IsOnWindows())
            {
                // Linux containers always start in placeholder mode
                System.Environment.SetEnvironmentVariable(SettingsKeys.PlaceholderMode, "1");
            }

            // Ensure that the WEBSITE_AUTH_ENCRYPTION_KEY is propagated to machine decryption key.
            string authEncryptionKey = System.Environment.GetEnvironmentVariable(SettingsKeys.AuthEncryptionKey);
            string machineDecryptionKey = System.Environment.GetEnvironmentVariable(SettingsKeys.MachineDecryptionKey);
            if (authEncryptionKey != null && machineDecryptionKey == null)
            {
                System.Environment.SetEnvironmentVariable(SettingsKeys.MachineDecryptionKey, authEncryptionKey);
            }
        }
    }
}