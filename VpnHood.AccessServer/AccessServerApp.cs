using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using VpnHood.AccessServer.Authentication;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using VpnHood.Common;
using VpnHood.Common.Logging;

namespace VpnHood.AccessServer;

public class AccessServerApp : AppBaseNet<AccessServerApp>
{
    private bool _designMode;
    private bool _recreateDb;
    private bool _testMode;
    public string ConnectionString { get; set; } = null!;
    public string ReportConnectionString { get; set; } = null!;
    public Uri AgentUrl { get; set; } = null!;
    public TimeSpan ServerUpdateStatusInterval { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan LostServerThreshold => ServerUpdateStatusInterval * 3;
    public AuthProviderItem RobotAuthItem { get; set; } = null!;
    public bool AutoMaintenance { get; set; }

    public AccessServerApp() : base("VpnHoodAccessServer")
    {
        // create logger
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(NLogConfigFilePath));
        VhLogger.Instance = loggerFactory.CreateLogger("NLog");
    }

    public void Configure(IConfiguration configuration)
    {
        var reportConnectionStringKey = _recreateDb ? "VhReportDatabaseRecreate" : "VhReportDatabase";
        
        //load settings
        ConnectionString = configuration.GetConnectionString("VhDatabase") ?? throw new InvalidOperationException($"Could not read {nameof(ConnectionString)} from settings.");
        ReportConnectionString = configuration.GetConnectionString(reportConnectionStringKey) ?? throw new InvalidOperationException($"Could not read {reportConnectionStringKey} from settings.");
        ServerUpdateStatusInterval = TimeSpan.FromSeconds(configuration.GetValue(nameof(ServerUpdateStatusInterval), ServerUpdateStatusInterval.TotalSeconds));
        AutoMaintenance = configuration.GetValue<bool>(nameof(AutoMaintenance));
        var authProviderItems = configuration.GetSection("AuthProviders").Get<AuthProviderItem[]>() ?? Array.Empty<AuthProviderItem>();
        RobotAuthItem = authProviderItems.Single(x => x.Schema == "Robot");

        var agentUrl = configuration.GetValue(nameof(AgentUrl), "");
        AgentUrl = !string.IsNullOrEmpty(agentUrl) ? new Uri(agentUrl) : throw new InvalidOperationException($"Could not read {nameof(AgentUrl)} from settings.");

        if (!_designMode)
            InitDatabase().Wait();
    }

    public async Task InitDatabase()
    {
        await using var vhContext = new VhContext();

        // recreate db
        if (_recreateDb)
        {
            VhLogger.Instance.LogInformation("Recreating the main database...");
            await vhContext.Database.EnsureDeletedAsync();
            await vhContext.Database.EnsureCreatedAsync();

            VhLogger.Instance.LogInformation("Recreating the report Database...");
            await using var vhReportContext = new VhReportContext();
            await vhReportContext.Database.EnsureDeletedAsync();
            await vhReportContext.Database.EnsureCreatedAsync();
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