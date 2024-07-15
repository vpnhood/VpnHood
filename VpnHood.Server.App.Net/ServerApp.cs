using System.Net;
using System.Runtime.InteropServices;
using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server.Access.Managers;
using VpnHood.Server.Access.Managers.File;
using VpnHood.Server.Access.Managers.Http;
using VpnHood.Server.App.SystemInformation;
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
    private readonly ITracker _tracker;
    private readonly CommandListener _commandListener;
    private VpnHoodServer? _vpnHoodServer;
    private FileStream? _lockStream;
    private bool _disposed;

    public IAccessManager AccessManager { get; }
    public FileAccessManager? FileAccessManager => AccessManager as FileAccessManager;
    public static string AppName => "VpnHoodServer";
    public static string AppFolderPath => Path.GetDirectoryName(typeof(ServerApp).Assembly.Location) ?? throw new Exception($"Could not acquire {nameof(AppFolderPath)}!");
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
        if (!File.Exists(appSettingsFilePath)) appSettingsFilePath = Path.Combine(AppFolderPath, "appsettings.json");
        AppSettings = File.Exists(appSettingsFilePath)
            ? VhUtil.JsonDeserialize<AppSettings>(File.ReadAllText(appSettingsFilePath))
            : new AppSettings();

        // Init File Logger before starting server
        VhLogger.IsDiagnoseMode = AppSettings.IsDiagnoseMode;
        InitFileLogger(storagePath);

        //create command Listener
        _commandListener = new CommandListener(Path.Combine(storagePath, FileNameAppCommand));
        _commandListener.CommandReceived += CommandListener_CommandReceived;

        // create access server
        AccessManager = AppSettings.HttpAccessManager != null
            ? CreateHttpAccessManager(AppSettings.HttpAccessManager)
            : CreateFileAccessManager(StoragePath, AppSettings.FileAccessManager);

        // tracker
        var anonyClientId = GetServerId(Path.Combine(InternalStoragePath, "server-id")).ToString();
        _tracker = new Ga4TagTracker
        {
            // ReSharper disable once StringLiteralTypo
            MeasurementId = "G-9SWLGEX6BT",
            SessionCount = 1,
            ClientId = anonyClientId,
            SessionId = Guid.NewGuid().ToString(),
            IsEnabled = AppSettings.AllowAnonymousTracker,
            UserProperties = new Dictionary<string, object>
            {
                { "server_version", VpnHoodServer.ServerVersion },
                { "access_manager", AccessManager.GetType().Name } }
        };
    }

    private void InitFileLogger(string storagePath)
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
            LogManager.Configuration.Variables["mydir"] = storagePath;
            VhLogger.Instance = loggerFactory.CreateLogger("NLog");
        }
        else
        {
            VhLogger.Instance.LogWarning("Could not find NLog file. ConfigFilePath: {configFilePath}", configFilePath);
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
    public static Guid GetServerId(string serverIdFile)
    {
        if (File.Exists(serverIdFile) && Guid.TryParse(File.ReadAllText(serverIdFile), out var serverId))
            return serverId;

        serverId = Guid.NewGuid();
        File.WriteAllText(serverIdFile, serverId.ToString());
        return serverId;
    }


    private static FileAccessManager CreateFileAccessManager(string storageFolderPath, FileAccessManagerOptions? options)
    {
        options ??= new FileAccessManagerOptions();
        options.PublicEndPoints ??= GetDefaultPublicEndPoints(options.TcpEndPointsValue);

        var accessManagerFolder = Path.Combine(storageFolderPath, "access");
        VhLogger.Instance.LogInformation($"Using FileAccessManager. AccessFolder: {accessManagerFolder}");
        var ret = new FileAccessManager(accessManagerFolder, options);
        return ret;
    }

    private static IPEndPoint[] GetDefaultPublicEndPoints(IEnumerable<IPEndPoint> tcpEndPoints)
    {
        var publicIps = IPAddressUtil.GetPublicIpAddresses().Result;
        var defaultPublicEps = new List<IPEndPoint>();
        var allListenerPorts = tcpEndPoints
            .Select(x => x.Port)
            .Distinct();

        foreach (var port in allListenerPorts)
            defaultPublicEps.AddRange(publicIps.Select(x => new IPEndPoint(x, port)));

        return defaultPublicEps.ToArray();
    }

    private static HttpAccessManager CreateHttpAccessManager(HttpAccessManagerOptions options)
    {
        VhLogger.Instance.LogInformation("Initializing HttpAccessManager. BaseUrl: {BaseUrl}", options.BaseUrl);
        var httpAccessManager = new HttpAccessManager(options)
        {
            Logger = VhLogger.Instance,
            LoggerEventId = GeneralEventId.AccessManager
        };
        return httpAccessManager;
    }

    private void CommandListener_CommandReceived(object? sender, CommandReceivedEventArgs e)
    {
        if (!VhUtil.IsNullOrEmpty(e.Arguments) && e.Arguments[0] == "stop")
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
            VhLogger.Instance.LogInformation("Sending stop server request...");
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
        cmdApp.OnExecuteAsync(async cancellationToken =>
        {
            // LogAnonymizer is on by default
            VhLogger.IsAnonymousMode = AppSettings.ServerConfig?.LogAnonymizerValue ?? true;

            // find listener port
            if (IsAnotherInstanceRunning())
                throw new AnotherInstanceIsRunning();

            // check FileAccessManager
            if (FileAccessManager != null && FileAccessManager.AccessItem_Count() == 0)
                VhLogger.Instance.LogWarning(
                    "There is no token in the store! Use the following command to create one:\n " +
                    "dotnet VpnHoodServer.dll gen -?");

            // Init logger for http access manager
            if (AccessManager is HttpAccessManager httpAccessManager)
                httpAccessManager.Logger = VhLogger.Instance;

            // systemInfoProvider
            ISystemInfoProvider systemInfoProvider = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new LinuxSystemInfoProvider()
                : new WinSystemInfoProvider();

            // run server
            _vpnHoodServer = new VpnHoodServer(AccessManager, new ServerOptions
            {
                Tracker = _tracker,
                SystemInfoProvider = systemInfoProvider,
                StoragePath = InternalStoragePath,
                Config = AppSettings.ServerConfig
            });

            // Command listener
            _commandListener.Start();

            // start server
            await _vpnHoodServer.Start().VhConfigureAwait();
            while (_vpnHoodServer.State != ServerState.Disposed)
                await Task.Delay(1000, cancellationToken).VhConfigureAwait();
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
        if (args.Length == 0) args = ["start"];
        var cmdApp = new CommandLineApplication
        {
            AllowArgumentSeparator = true,
            Name = AppName,
            FullName = "VpnHood server",
            MakeSuggestionsInErrorMessage = true
        };

        cmdApp.HelpOption(true);
        cmdApp.VersionOption("-n|--version", VpnHoodServer.ServerVersion.ToString(3));

        cmdApp.Command("start", StartServer);
        cmdApp.Command("stop", StopServer);

        if (FileAccessManager != null)
            new FileAccessManagerCommand(FileAccessManager)
                .AddCommands(cmdApp);

        await cmdApp.ExecuteAsync(args).VhConfigureAwait();
    }
}