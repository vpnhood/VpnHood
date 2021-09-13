using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using VpnHood.AccessServer.Auth.Models;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using VpnHood.Common;
using VpnHood.Common.Logging;

namespace VpnHood.AccessServer
{
    public class AccessServerApp : AppBaseNet<AccessServerApp>
    {
        public string ConnectionString { get; set; } = null!;
       
        public AccessServerApp() : base("VpnHoodAccessServer")
        {
            // create logger
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(NLogConfigFilePath));
            VhLogger.Instance = loggerFactory.CreateLogger("NLog");
        }

        public void Configure(IConfiguration configuration)
        {
            //load settings
            ConnectionString = configuration.GetConnectionString("VhDatabase") ?? throw new InvalidOperationException($"Could not read {nameof(ConnectionString)} from settings");

            InitDatabase().Wait();
        }

        public async Task InitDatabase()
        {
            await SecurityUtil.Init();
        }

        protected override void OnStart(string[] args)
        {
            if (args.Contains("/designmode"))
            {
                VhLogger.Instance.LogInformation("Skipping normal startup due DesignMode!");
                return;
            }

            if (args.Contains("/testmode"))
            {
                VhLogger.Instance.LogInformation("Skipping normal startup due TestMode!");
                CreateHostBuilder(args).Build();
                return;
            }

            if (IsAnotherInstanceRunning($"{AppName}:single"))
                throw new InvalidOperationException("Another instance is running and listening!");

            CreateHostBuilder(args)
                .Build()
                .Run();
        }

        public IHostBuilder CreateHostBuilder(string[] args)
        {
            return Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); })
                .ConfigureLogging((hostingContext, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddNLog(hostingContext.Configuration.GetSection("Logging"));
                });
        }
    }
}