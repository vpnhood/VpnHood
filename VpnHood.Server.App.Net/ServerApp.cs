using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Trackers;
using VpnHood.Common.Logging;
using VpnHood.Server.App.SystemInformation;
using VpnHood.Server.SystemInformation;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using VpnHood.Server.Providers.FileAccessServerProvider;
using VpnHood.Server.Providers.HttpAccessServerProvider;

namespace VpnHood.Server.App;

public class ServerApp : AppBaseNet<ServerApp>
{
    private const string FileNamePublish = "publish.json";
    private const string FileNameAppCommand = "appcommand";
    private const string FolderNameStorage = "storage";
    private const string FolderNameInternal = "internal";
    private readonly GoogleAnalyticsTracker _googleAnalytics;
    private readonly CommandListener _commandListener;
    private VpnHoodServer? _vpnHoodServer;
    public AppSettings AppSettings { get; }

    public static string StoragePath => Directory.GetCurrentDirectory();
    public string InternalStoragePath { get; }

    public ServerApp() : base("VpnHoodServer")
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger();
        AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

        // set storage folder
        var parentAppFolderPath = Path.GetDirectoryName(Path.GetDirectoryName(typeof(ServerApp).Assembly.Location));
        var storagePath = (parentAppFolderPath != null && File.Exists(Path.Combine(parentAppFolderPath, FileNamePublish)))
            ? Path.Combine(parentAppFolderPath, FolderNameStorage)
            : Path.Combine(Directory.GetCurrentDirectory(), FolderNameStorage);
        Directory.CreateDirectory(storagePath);
        Directory.SetCurrentDirectory(storagePath);
            
        // internal folder
        InternalStoragePath = Path.Combine(storagePath, FolderNameInternal);
        Directory.CreateDirectory(InternalStoragePath);

        // load app settings
        var appSettingsFilePath = Path.Combine(StoragePath, "appsettings.debug.json");
        if (!File.Exists(appSettingsFilePath)) appSettingsFilePath = Path.Combine(StoragePath, "appsettings.json");
        if (!File.Exists(appSettingsFilePath)) //todo legacy for 318 and older
        {
            var oldSettingsFile = Path.Combine(Path.GetDirectoryName(storagePath)!, "appsettings.json");
            if (File.Exists(oldSettingsFile))
            {
                appSettingsFilePath = Path.Combine(StoragePath, "appsettings.json");
                File.Copy(oldSettingsFile, appSettingsFilePath);
            }
        }
        if (!File.Exists(appSettingsFilePath)) appSettingsFilePath = Path.Combine(AppFolderPath, "appsettings.json");
        AppSettings = File.Exists(appSettingsFilePath)
            ? Util.JsonDeserialize<AppSettings>(File.ReadAllText(appSettingsFilePath))
            : new AppSettings();
        VhLogger.IsDiagnoseMode = AppSettings.IsDiagnoseMode;

        // logger
        var configFilePath = Path.Combine(StoragePath, "NLog.config");
        if (!File.Exists(configFilePath)) configFilePath = Path.Combine(AppFolderPath, "NLog.config");
        if (File.Exists(configFilePath))
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddNLog(configFilePath);
                if (AppSettings.IsDiagnoseMode)
                    builder.SetMinimumLevel(LogLevel.Trace);
            });
            LogManager.Configuration.Variables["mydir"] = StoragePath;
            VhLogger.Instance = loggerFactory.CreateLogger("NLog");
        }
        else
        {
            VhLogger.Instance.LogWarning($"Could not find NLog file. {configFilePath}");
        }

        //create command Listener
        _commandListener = new CommandListener(Path.Combine(storagePath, FileNameAppCommand));
        _commandListener.CommandReceived += CommandListener_CommandReceived;

        // tracker
        var anonyClientId = GetServerId(InternalStoragePath).ToString();
        _googleAnalytics = new GoogleAnalyticsTracker(
            "UA-183010362-1",
            anonyClientId,
            typeof(ServerApp).Assembly.GetName().Name ?? "VpnHoodServer",
            typeof(ServerApp).Assembly.GetName().Version?.ToString() ?? "x")
        {
            IsEnabled = AppSettings.IsAnonymousTrackerEnabled
        };

        // create access server
        AccessServer = AppSettings.HttpAccessServer != null
            ? CreateHttpAccessServer(AppSettings.HttpAccessServer)
            : CreateFileAccessServer(StoragePath, AppSettings.FileAccessServer);
    }

    private void CurrentDomain_ProcessExit(object? sender, EventArgs e)
    {
        VhLogger.Instance.LogInformation("Syncing all sessions and terminating the server...");
        _vpnHoodServer?.SessionManager.SyncSessions().Wait();
        _vpnHoodServer?.Dispose();
    }

    public static Guid GetServerId(string storagePath)
    {
        var serverIdFile = Path.Combine(storagePath, "server-id");
        if (File.Exists(serverIdFile) && Guid.TryParse(File.ReadAllText(serverIdFile), out var serverId))
            return serverId;

        serverId = Guid.NewGuid();
        File.WriteAllText(serverIdFile, serverId.ToString());
        return serverId;
    }

    public IAccessServer AccessServer { get; }
    public FileAccessServer? FileAccessServer => AccessServer as FileAccessServer;

    private static FileAccessServer CreateFileAccessServer(string storageFolderPath, FileAccessServerOptions? options)
    {
        var accessServerFolder = Path.Combine(storageFolderPath, "access");
        VhLogger.Instance.LogInformation($"Using FileAccessServer. AccessFolder: {accessServerFolder}");
        var ret = new FileAccessServer(accessServerFolder, options ?? new FileAccessServerOptions());
        return ret;
    }

    private static HttpAccessServer CreateHttpAccessServer(HttpAccessServerOptions options)
    {
        VhLogger.Instance.LogInformation($"Initializing ResetAccessServer. {nameof(options.BaseUrl)}: {options.BaseUrl}");
        var ret = HttpAccessServer.Create(options);
        return ret;
    }

    private void CommandListener_CommandReceived(object? sender, CommandReceivedEventArgs e)
    {
        if (!Util.IsNullOrEmpty(e.Arguments) && e.Arguments[0] == "stop")
        {
            VhLogger.Instance.LogInformation("I have received the stop command!");
            _vpnHoodServer?.SessionManager.SyncSessions().Wait();
            _vpnHoodServer?.Dispose();
        }
    }

    private void StopServer(CommandLineApplication cmdApp)
    {
        cmdApp.Description = "Stop all instances of VpnHoodServer that running from this folder";
        cmdApp.OnExecute(() =>
        {
            Console.WriteLine("Sending stop server request...");
            _commandListener.SendCommand("stop");
        });
    }

    private void StartServer(CommandLineApplication cmdApp)
    {
        cmdApp.Description = "Run the server (default command)";
        cmdApp.OnExecute(() =>
        {
            // find listener port
            var instanceName = Util.GetStringMd5(typeof(ServerApp).Assembly.Location);
            if (IsAnotherInstanceRunning($"VpnHoodServer-{instanceName}"))
                throw new InvalidOperationException("Another instance is running!");

            // check FileAccessServer
            if (FileAccessServer != null && FileAccessServer.AccessItem_LoadAll().Length == 0)
                VhLogger.Instance.LogWarning(
                    "There is no token in the store! Use the following command to create one:\n " +
                    "dotnet VpnHoodServer.dll gen -?");

            // systemInfoProvider
            ISystemInfoProvider systemInfoProvider = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new LinuxSystemInfoProvider()
                : new WinSystemInfoProvider();

            // run server
            _vpnHoodServer = new VpnHoodServer(AccessServer, new ServerOptions
            {
                Tracker = _googleAnalytics,
                SystemInfoProvider = systemInfoProvider,
                SocketFactory = new ServerSocketFactory(),
                StoragePath = InternalStoragePath
            });

            // track
            _googleAnalytics.TrackEvent("Usage", "ServerRun");

            // Command listener
            _commandListener.Start();

            // start server
            _vpnHoodServer.Start().Wait();
            while (_vpnHoodServer.State != ServerState.Disposed)
                Thread.Sleep(1000);
            return 0;
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !Disposed)
            LogManager.Shutdown();

        base.Dispose(disposing);
    }

    protected override void OnStart(string[] args)
    {
        // replace "/?"
        for (var i = 0; i < args.Length; i++)
            if (args[i] == "/?")
                args[i] = "-?";

        // set default
        if (args.Length == 0) args = new[] { "start" };
        var cmdApp = new CommandLineApplication
        {
            AllowArgumentSeparator = true,
            Name = AppName,
            FullName = "VpnHood server",
            MakeSuggestionsInErrorMessage = true
        };

        cmdApp.HelpOption(true);
        cmdApp.VersionOption("-n|--version", AppVersion);

        cmdApp.Command("start", StartServer);
        cmdApp.Command("stop", StopServer);

        if (FileAccessServer != null)
            new FileAccessServerCommand(FileAccessServer)
                .AddCommands(cmdApp);

        cmdApp.Execute(args);
    }
}