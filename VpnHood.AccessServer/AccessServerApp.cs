using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using VpnHood.AccessServer.Auth;
using VpnHood.Common;
using VpnHood.Logging;

namespace VpnHood.AccessServer
{
    public class AccessServerApp : AppBaseNet<AccessServerApp>
    {
        public AccessServerApp() : base("VpnHoodAccessServer")
        {
            // create logger
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(NLogConfigFilePath));
            VhLogger.Instance = loggerFactory.CreateLogger("NLog");
        }

        public string ConnectionString { get; set; } = null!;
        public AuthProviderItem[] AuthProviderItems { get; set; } = null!;
        public string AdminUserId { get; set; } = null!;

        public void Configure(IConfiguration configuration)
        {
            //load settings
            AuthProviderItems = configuration.GetSection("AuthProviders").Get<AuthProviderItem[]>() ??
                                Array.Empty<AuthProviderItem>();
            AdminUserId = configuration.GetValue<string>("AgentUserId") ??
                          throw new InvalidOperationException($"Could not read {nameof(AdminUserId)} from settings");
            ConnectionString = configuration.GetValue<string>("ConnectionString") ??
                               throw new InvalidOperationException(
                                   $"Could not read {nameof(ConnectionString)} from settings");

            InitDatabase();
        }

        public void InitDatabase()
        {
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