using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Trackers;
using VpnHood.Common.Utils;
using VpnHood.Server.App.SystemInformation;
using VpnHood.Server.Providers.FileAccessServerProvider;
using VpnHood.Server.Providers.HttpAccessServerProvider;
using VpnHood.Server.SystemInformation;
using VpnHood.Tunneling;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace VpnHood.Server.App;

public class ServerApp : IDisposable
{
    private const string FileNamePublish = "publish.json";
    private const string FileNameAppCommand = "appcommand";
    private const string FolderNameStorage = "storage";
    private const string FolderNameInternal = "internal";
    private readonly GoogleAnalyticsTracker _googleAnalytics;
    private readonly CommandListener _commandListener;
    private VpnHoodServer? _vpnHoodServer;
    private FileStream? _lockStream;
    private bool _disposed;

    public string AppName => "VpnHoodServer";
    public static string AppFolderPath => Path.GetDirectoryName(typeof(ServerApp).Assembly.Location) ?? throw new Exception($"Could not acquire {nameof(AppFolderPath)}!");
    public string AppVersion => typeof(ServerApp).Assembly.GetName().Version?.ToString() ?? "*";
    public AppSettings AppSettings { get; }
    public static string StoragePath => Directory.GetCurrentDirectory();
    public string InternalStoragePath { get; }

    public ServerApp()
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
        if (!File.Exists(appSettingsFilePath)) //todo legacy for version 318 and older
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

    private void InitFileLogger()
    {
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
    }

    private void CurrentDomain_ProcessExit(object? sender, EventArgs e)
    {
        if (_vpnHoodServer != null)
        {
            VhLogger.Instance.LogInformation("Syncing all sessions and terminating the server...");
            _vpnHoodServer.SessionManager.SyncSessions().Wait();
            _vpnHoodServer.Dispose();
        }
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
        VhLogger.Instance.LogInformation("Initializing ResetAccessServer. BaseUrl: {BaseUrl}", options.BaseUrl);
        var httpAccessServer = new HttpAccessServer(options)
        {
            Logger = VhLogger.Instance,
            LoggerEventId = GeneralEventId.AccessServer
        };
        return httpAccessServer;
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

    private bool IsAnotherInstanceRunning()
    {
        var lockFile = Path.Combine(InternalStoragePath, "server.lock");
        try
        {
            _lockStream = File.OpenWrite(lockFile);
            var stream = new StreamWriter(_lockStream, leaveOpen: true);
            stream.WriteLine(DateTime.UtcNow);
            stream.Dispose();
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private void StartServer(CommandLineApplication cmdApp)
    {
        cmdApp.Description = "Run the server (default command)";
        cmdApp.OnExecuteAsync(async (cancellationToken) =>
        {
            // LogAnonymizer is on by default
            VhLogger.IsAnonymousMode = AppSettings.ServerConfig?.LogAnonymizerValue ?? true;
            
            // find listener port
            if (IsAnotherInstanceRunning())
                throw new AnotherInstanceIsRunning();

            // check FileAccessServer
            if (FileAccessServer != null && FileAccessServer.AccessItem_LoadAll().Length == 0)
                VhLogger.Instance.LogWarning(
                    "There is no token in the store! Use the following command to create one:\n " +
                    "dotnet VpnHoodServer.dll gen -?");

            // Init File Logger before starting server; other log should be on console or other file
            InitFileLogger();
            if (AccessServer is HttpAccessServer httpAccessServer)
                httpAccessServer.Logger = VhLogger.Instance;

            // systemInfoProvider
            ISystemInfoProvider systemInfoProvider = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new LinuxSystemInfoProvider()
                : new WinSystemInfoProvider();

            // run server
            _vpnHoodServer = new VpnHoodServer(AccessServer, new ServerOptions
            {
                Tracker = _googleAnalytics,
                SystemInfoProvider = systemInfoProvider,
                StoragePath = InternalStoragePath,
                Config = AppSettings.ServerConfig
            });

            // track
            _ = _googleAnalytics.TrackEvent("Usage", "ServerRun");

            // Command listener
            _commandListener.Start();

            // start server
            await _vpnHoodServer.Start();
            while (_vpnHoodServer.State != ServerState.Disposed)
                await Task.Delay(1000, cancellationToken);
            return 0;
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        LogManager.Shutdown();
    }

    public async Task Start(string[] args)
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

        await cmdApp.ExecuteAsync(args);
    }
}