using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using VpnHood.Common;
using VpnHood.Common.Logging;

namespace VpnHood.AccessServer
{
    public class AccessServerApp : AppBaseNet<AccessServerApp>
    {
        private bool _designMode;
        private bool _recreateDb;
        private bool _testMode;
        public string ConnectionString { get; set; } = null!;
        public int MaxUserProjectCount { get; set; } = 5;
       
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
            MaxUserProjectCount = configuration.GetValue(nameof(MaxUserProjectCount), MaxUserProjectCount);

            if (!_designMode)
                InitDatabase().Wait();
        }

        public async Task InitDatabase()
        {
            await using VhContext vhContext = new();

            // recreate db
            if (_recreateDb)
            {
                VhLogger.Instance.LogInformation("Recreating database...");
                await vhContext.Database.EnsureDeletedAsync();
                await vhContext.Database.EnsureCreatedAsync();
            }

            if (!_testMode)
            {
                VhLogger.Instance.LogInformation("Initializing database...");
                await vhContext.Init(SecureObjectTypes.All, Permissions.All, PermissionGroups.All);
            }
        }

        protected override void OnStart(string[] args)
        {
            _testMode = args.Contains("/testmode");
            _recreateDb = args.Contains("/recreatedb");
            _designMode = args.Contains("/designmode");

            if (_designMode)
            {
                VhLogger.Instance.LogInformation("Skipping normal startup due DesignMode!");
                return;
            }

            if (_testMode)
            {
                VhLogger.Instance.LogInformation("Skipping normal startup due TestMode!");
                CreateHostBuilder(args).Build();
                return;
            }

            if (_recreateDb)
            {
                VhLogger.Instance.LogInformation("Skipping normal startup due Recreating Database!");
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