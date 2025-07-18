﻿using System.Net;
using System.Runtime.InteropServices;
using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using VpnHood.App.Server.Providers.Linux;
using VpnHood.App.Server.Providers.Win;
using VpnHood.Core.Common;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Managers.FileAccessManagement;
using VpnHood.Core.Server.Access.Managers.HttpAccessManagers;
using VpnHood.Core.Server.SystemInformation;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.LinuxTun;

namespace VpnHood.App.Server;

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

    public static string AppFolderPath =>
        Path.GetDirectoryName(typeof(ServerApp).Assembly.Location) ??
        throw new Exception($"Could not acquire {nameof(AppFolderPath)}.");

    public AppSettings AppSettings { get; }
    public static string StoragePath => Directory.GetCurrentDirectory();
    public string InternalStoragePath { get; }

    public ServerApp()
    {
        VhLogger.Instance = VhLogger.CreateConsoleLogger();
        AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

        // set storage folder
        var parentAppFolderPath = Path.GetDirectoryName(Path.GetDirectoryName(typeof(ServerApp).Assembly.Location));
        var storagePath = parentAppFolderPath != null && File.Exists(Path.Combine(parentAppFolderPath, FileNamePublish))
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
            ? JsonUtils.Deserialize<AppSettings>(File.ReadAllText(appSettingsFilePath))
            : new AppSettings();

        // Init File Logger before starting server
        VhLogger.MinLogLevel = AppSettings.LogLevel;

        //create command Listener
        _commandListener = new CommandListener(Path.Combine(storagePath, FileNameAppCommand));
        _commandListener.CommandReceived += CommandListener_CommandReceived;

        // create access server
        AccessManager = AppSettings.HttpAccessManager != null
            ? CreateHttpAccessManager(AppSettings.HttpAccessManager)
            : CreateFileAccessManager(StoragePath, AppSettings.FileAccessManager);

        // tracker
        var anonyClientId = GetServerId(Path.Combine(InternalStoragePath, "server-id")).ToString();
        _tracker = new Ga4TagTracker {
            // ReSharper disable once StringLiteralTypo
            MeasurementId = "G-9SWLGEX6BT",
            SessionCount = 1,
            ClientId = anonyClientId,
            SessionId = Guid.NewGuid().ToString(),
            IsEnabled = AppSettings.AllowAnonymousTracker,
            UserProperties = new Dictionary<string, object> {
                { "server_version", VpnHoodServer.ServerVersion },
                { "access_manager", AccessManager.GetType().Name }
            }
        };
    }

    private static void InitFileLogger(string storagePath)
    {
        var configFilePath = Path.Combine(StoragePath, "NLog.config");
        if (!File.Exists(configFilePath)) configFilePath = Path.Combine(AppFolderPath, "NLog.config");
        if (File.Exists(configFilePath)) {
            using var loggerFactory = LoggerFactory.Create(builder => {
                builder.AddNLog(configFilePath);
            });
            if (LogManager.Configuration != null)
                LogManager.Configuration.Variables["mydir"] = storagePath;
            VhLogger.Instance = loggerFactory.CreateLogger("VpnHood");
        }
        else {
            VhLogger.Instance.LogWarning("Could not find NLog file. ConfigFilePath: {configFilePath}", configFilePath);
        }
    }

    private void CurrentDomain_ProcessExit(object? sender, EventArgs e)
    {
        if (_vpnHoodServer != null) {
            VhLogger.Instance.LogInformation("Syncing all sessions and terminating the server...");
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

    private static FileAccessManager CreateFileAccessManager(string storageFolderPath,
        FileAccessManagerOptions? options)
    {
        options ??= new FileAccessManagerOptions();
        options.PublicEndPoints ??= GetDefaultPublicEndPoints(options.TcpEndPointsValue, CancellationToken.None).Result;

        var accessManagerFolder = Path.Combine(storageFolderPath, "access");
        VhLogger.Instance.LogInformation("Using FileAccessManager. AccessFolder: {AccessManagerFolder}", accessManagerFolder);
        var ret = new FileAccessManager(accessManagerFolder, options);
        return ret;
    }

    private static async Task<IPEndPoint[]> GetDefaultPublicEndPoints(IEnumerable<IPEndPoint> tcpEndPoints,
        CancellationToken cancellationToken)
    {
        var publicIps = await IPAddressUtil.GetPublicIpAddresses(cancellationToken);
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
        var httpAccessManager = new HttpAccessManager(options) {
            Logger = VhLogger.Instance,
            LoggerEventId = GeneralEventId.AccessManager
        };
        return httpAccessManager;
    }

    private void CommandListener_CommandReceived(object? sender, CommandReceivedEventArgs e)
    {
        if (VhUtils.IsNullOrEmpty(e.Arguments))
            return;

        if (e.Arguments[0] == "stop") {
            VhLogger.Instance.LogInformation("I have received the stop command!");
            _vpnHoodServer?.Dispose();
        }

        if (e.Arguments[0] == "gc") {
            VhLogger.Instance.LogInformation("I have received the gc command!");
            VhLogger.Instance.LogInformation("[GC] Before: TotalMemory: {TotalMemory}, TotalAllocatedBytes: {TotalAllocatedBytes}",
                VhUtils.FormatBytes(GC.GetTotalMemory(forceFullCollection: false)), VhUtils.FormatBytes(GC.GetTotalAllocatedBytes()));

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            VhLogger.Instance.LogInformation("[GC] After: TotalMemory: {TotalMemory}, TotalAllocatedBytes: {TotalAllocatedBytes}",
                VhUtils.FormatBytes(GC.GetTotalMemory(forceFullCollection: false)), VhUtils.FormatBytes(GC.GetTotalAllocatedBytes()));

            // Report session manager
            _vpnHoodServer?.SessionManager.Report();
        }

    }

    private void RunGc(CommandLineApplication cmdApp)
    {
        cmdApp.Description = "Run Garbage Collector for debugging purpose.";
        cmdApp.OnExecute(() => {
            VhLogger.Instance.LogInformation("Sending GC request...");
            _commandListener.SendCommand("gc");
        });
    }

    private void StopServer(CommandLineApplication cmdApp)
    {
        cmdApp.Description = "Stop all instances of VpnHoodServer that running from this folder";
        cmdApp.OnExecute(() => {
            VhLogger.Instance.LogInformation("Sending stop server request...");
            _commandListener.SendCommand("stop");
        });
    }

    private bool IsAnotherInstanceRunning()
    {
        var lockFile = Path.Combine(InternalStoragePath, "server.lock");
        try {
            _lockStream = File.OpenWrite(lockFile);
            var stream = new StreamWriter(_lockStream, leaveOpen: true);
            stream.WriteLine(DateTime.UtcNow);
            stream.Dispose();
            return false;
        }
        catch (IOException) {
            return true;
        }
    }

    private void StartServer(CommandLineApplication cmdApp)
    {
        cmdApp.Description = "Run the server (default command)";
        cmdApp.OnExecuteAsync(async cancellationToken => {
            // LogAnonymizer is on by default
            VhLogger.IsAnonymousMode = AppSettings.ServerConfig?.LogAnonymizerValue ?? true;

            // find listener port
            if (IsAnotherInstanceRunning())
                throw new AnotherInstanceIsRunning();

            // initialize logger
            InitFileLogger(StoragePath);

            // check FileAccessManager
            if (FileAccessManager != null && await FileAccessManager.AccessTokenService.GetTotalCount() == 0)
                VhLogger.Instance.LogWarning(
                    "There is no token in the store! Use the following command to create one:\n " +
                    "dotnet VpnHoodServer.dll gen -?");

            // Init logger for http access manager
            if (AccessManager is HttpAccessManager httpAccessManager)
                httpAccessManager.Logger = VhLogger.Instance;

            // SystemInfoProvider
            ISystemInfoProvider systemInfoProvider = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new LinuxSystemInfoProvider()
                : new WinSystemInfoProvider();

            // NetConfigurationProvider
            INetConfigurationProvider? configurationProvider = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new LinuxNetConfigurationProvider(VhLogger.Instance)
                : null;

            ISwapMemoryProvider? swapMemoryProvider = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new LinuxSwapMemoryProvider(VhLogger.Instance)
                : null;

            // run server
            var virtualIpNetworkV4 = TunnelDefaults.VirtualIpNetworkV4;
            var virtualIpNetworkV6 = TunnelDefaults.VirtualIpNetworkV6;
            _vpnHoodServer = new VpnHoodServer(AccessManager, new ServerOptions {
                Tracker = _tracker,
                VpnAdapter = await CreateTunProvider(virtualIpNetworkV4, virtualIpNetworkV6, cancellationToken),
                SystemInfoProvider = systemInfoProvider,
                NetConfigurationProvider = configurationProvider,
                SwapMemoryProvider = swapMemoryProvider,
                StoragePath = InternalStoragePath,
                Config = AppSettings.ServerConfig,
                VirtualIpNetworkV4 = virtualIpNetworkV4,
                VirtualIpNetworkV6 = virtualIpNetworkV6
            });

            // Command listener
            _commandListener.Start();

            // start server
            await _vpnHoodServer.Start().Vhc();
            while (_vpnHoodServer.State != ServerState.Disposed)
                await Task.Delay(1000, cancellationToken).Vhc();
            return 0;
        });
    }

    private static async Task<IVpnAdapter?> CreateTunProvider(IpNetwork virtualIpNetworkV4, IpNetwork virtualIpNetworkV6, CancellationToken cancellationToken)
    {
        try {
            var vpnAdapter = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new LinuxTunVpnAdapter(new LinuxVpnAdapterSettings {
                    AdapterName = "VpnHoodServer",
                    Blocking = false,
                    AutoDisposePackets = true
                })
                : null;

            // start the adapter
            if (vpnAdapter != null) {
                VhLogger.Instance.LogInformation("Starting VpnAdapter...");
                await vpnAdapter.Start(new VpnAdapterOptions {
                    SessionName = "VpnHoodServer",
                    Mtu = TunnelDefaults.Mtu,
                    UseNat = true,
                    VirtualIpNetworkV4 = virtualIpNetworkV4,
                    VirtualIpNetworkV6 = virtualIpNetworkV6,
                }, cancellationToken);
            }

            return vpnAdapter;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Failed to create the VpnAdapter. Using proxy only.");
            return null;
        }
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
        var cmdApp = new CommandLineApplication {
            AllowArgumentSeparator = true,
            Name = AppName,
            FullName = "VpnHood server",
            MakeSuggestionsInErrorMessage = true
        };

        cmdApp.HelpOption(true);
        cmdApp.VersionOption("-n|--version", VpnHoodServer.ServerVersion.ToString(3));

        cmdApp.Command("start", StartServer);
        cmdApp.Command("stop", StopServer);
        cmdApp.Command("gc", RunGc);

        if (FileAccessManager != null)
            new FileAccessManagerCommand(FileAccessManager)
                .AddCommands(cmdApp);

        await cmdApp.ExecuteAsync(args).Vhc();
    }
}