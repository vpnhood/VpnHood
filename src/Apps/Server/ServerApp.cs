using System.CommandLine;
using System.Net;
using System.Runtime.InteropServices;
using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using Microsoft.Extensions.Logging;
using VpnHood.App.Server.Providers.Linux;
using VpnHood.App.Server.Providers.Win;
using VpnHood.Core.Common;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.Access.Managers.FileAccessManagers;
using VpnHood.Core.Server.Access.Managers.HttpAccessManagers;
using VpnHood.Core.Server.SystemInformation;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.LinuxTun;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.App.Server;

public class ServerApp : IDisposable
{
    private const string FileNamePublish = "publish.json";
    private const string FileNameAppCommand = "appcommand";
    private const string FolderNameStorage = "storage";
    private const string FolderNameInternal = "internal";
    private const string EnvNameAppSettings = "VH_APPSETTINGS";
    private readonly ITracker _tracker;
    private readonly Lazy<IAccessManager> _accessManager;
    private readonly CommandListener _commandListener;
    private readonly CancellationTokenSource _stopCts = new(); // never disposed: a late stop must not hit a disposed source
    private IReadOnlyList<PosixSignalRegistration> _signalRegistrations = []; // held for the process's life: a collected one unregisters
    private VpnHoodServer? _vpnHoodServer;
    private IVpnAdapter? _vpnAdapter;
    private FileStream? _lockStream;
    private ServerLog? _serverLog;
    private bool _disposed;
    private readonly string? _downloadsPath;
    private readonly string _appSettingsSource;

    public IAccessManager AccessManager => _accessManager.Value;
    public FileAccessManager? FileAccessManager => AccessManager as FileAccessManager;
    public static string AppName => "VpnHoodServer";
    public static string AppId => "com.vpnhood.server";
    public static string AppFolderPath =>
        Path.GetDirectoryName(typeof(ServerApp).Assembly.Location) ??
        throw new Exception($"Could not acquire {nameof(AppFolderPath)}.");

    public AppSettings AppSettings { get; }
    public static string StoragePath => Directory.GetCurrentDirectory();
    public string InternalStoragePath { get; }

    public ServerApp()
    {
        // An installed server runs from <install>/bin/<version>, its install's publish.json and storage
        // two folders up; any other run, a debugger's say, keeps its storage in the working folder.
        var installFolderPath = Path.GetDirectoryName(Path.GetDirectoryName(AppFolderPath));
        var storagePath = installFolderPath != null && File.Exists(Path.Combine(installFolderPath, FileNamePublish))
            ? Path.Combine(installFolderPath, FolderNameStorage)
            : Path.Combine(Directory.GetCurrentDirectory(), FolderNameStorage);
        Directory.CreateDirectory(storagePath);
        Directory.SetCurrentDirectory(storagePath);

        // internal folder
        InternalStoragePath = Path.Combine(storagePath, FolderNameInternal);
        Directory.CreateDirectory(InternalStoragePath);

        (AppSettings, _appSettingsSource) = LoadAppSettings();

        // set downloads path for diagnose
        _downloadsPath = AppSettings.DownloadsPath ?? Path.Combine(storagePath, "downloads");
        if (!string.IsNullOrWhiteSpace(_downloadsPath))
            Directory.CreateDirectory(_downloadsPath);

        // Init File Logger before starting server
        VhLogger.MinLogLevel = AppSettings.LogLevel;

        //create command Listener
        _commandListener = new CommandListener(Path.Combine(storagePath, FileNameAppCommand));
        _commandListener.CommandReceived += CommandListener_CommandReceived;

        // The access manager is made at its first use, since its making logs: at start after the
        // log exists, so its lines are in the log; by a token command, on the terminal.
        _accessManager = new Lazy<IAccessManager>(() => AppSettings.HttpAccessManager != null
            ? CreateHttpAccessManager(AppSettings.HttpAccessManager)
            : CreateFileAccessManager(StoragePath, AppSettings.FileAccessManager, CancellationToken.None).Result);

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
                { "access_manager", AppSettings.HttpAccessManager != null ? nameof(HttpAccessManager) : nameof(FileAccessManager) }
            }
        };
    }

    // VH_APPSETTINGS, when set, replaces the settings file whole: a Docker-only service has no installer
    // to write one. Malformed, it fails the start, as a malformed file does. The source goes to the log,
    // since a file beside a set variable is ignored.
    private static (AppSettings AppSettings, string Source) LoadAppSettings()
    {
        var appSettingsJson = Environment.GetEnvironmentVariable(EnvNameAppSettings);
        if (!string.IsNullOrWhiteSpace(appSettingsJson)) {
            try {
                return (JsonUtils.Deserialize<AppSettings>(appSettingsJson), EnvNameAppSettings);
            }
            catch (Exception ex) {
                throw new InvalidDataException($"{EnvNameAppSettings} holds no valid settings. {ex.Message}", ex);
            }
        }

        var appSettingsFilePath = Path.Combine(StoragePath, "appsettings.debug.json");
        if (!File.Exists(appSettingsFilePath)) appSettingsFilePath = Path.Combine(StoragePath, "appsettings.json");
        if (!File.Exists(appSettingsFilePath)) appSettingsFilePath = Path.Combine(AppFolderPath, "appsettings.json");
        return File.Exists(appSettingsFilePath)
            ? (JsonUtils.Deserialize<AppSettings>(File.ReadAllText(appSettingsFilePath)), appSettingsFilePath)
            : (new AppSettings(), "the defaults");
    }

    public static Guid GetServerId(string serverIdFile)
    {
        if (File.Exists(serverIdFile) && Guid.TryParse(File.ReadAllText(serverIdFile), out var serverId))
            return serverId;

        serverId = Guid.NewGuid();
        File.WriteAllText(serverIdFile, serverId.ToString());
        return serverId;
    }

    private static async Task<FileAccessManager> CreateFileAccessManager(string storageFolderPath,
        FileAccessManagerOptions? options, CancellationToken cancellationToken)
    {
        options ??= new FileAccessManagerOptions();
        options.PublicEndPoints ??= await GetDefaultPublicEndPoints(options.TcpEndPointsValue, cancellationToken);

        var accessManagerFolder = Path.Combine(storageFolderPath, "access");
        VhLogger.Instance.LogInformation("Using FileAccessManager. AccessFolder: {AccessManagerFolder}",
            accessManagerFolder);
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

        return [.. defaultPublicEps];
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

        // it only cancels: the running start command then stops the server, as on a signal
        if (e.Arguments[0] == "stop") {
            VhLogger.Instance.LogInformation("I have received the stop command!");
            _stopCts.TryCancel();
        }

        if (e.Arguments[0] == "gc") {
            VhLogger.Instance.LogInformation("I have received the gc command!");
            VhLogger.Instance.LogInformation(
                "[GC] Before: TotalMemory: {TotalMemory}, TotalAllocatedBytes: {TotalAllocatedBytes}",
                VhUtils.FormatBytes(GC.GetTotalMemory(forceFullCollection: false)),
                VhUtils.FormatBytes(GC.GetTotalAllocatedBytes()));

            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            VhLogger.Instance.LogInformation(
                "[GC] After: TotalMemory: {TotalMemory}, TotalAllocatedBytes: {TotalAllocatedBytes}",
                VhUtils.FormatBytes(GC.GetTotalMemory(forceFullCollection: false)),
                VhUtils.FormatBytes(GC.GetTotalAllocatedBytes()));

            // Report session manager
            _vpnHoodServer?.SessionManager.Report();
        }
    }

    private Command CreateGcCommand()
    {
        var command = new Command("gc", "Run Garbage Collector for debugging purpose.") {
            Hidden = true
        };

        command.SetAction(_ => {
            Console.WriteLine("Sending GC request...");
            _commandListener.SendCommand("gc");
            return Task.CompletedTask;
        });
        return command;
    }

    private Command CreateStopCommand()
    {
        var command = new Command("stop",
            "Stop all instances of VpnHoodServer that running from this folder");
        command.SetAction(async (_, cancellationToken) => {
            Console.WriteLine("Sending stop server request...");
            _commandListener.SendCommand("stop");

            // Returns once the server has exited, as systemd expects of ExecStop: right after it,
            // systemd signals every process left in the unit, the cleanup's ip and iptables too.
            if (!await WaitForInstanceExit(TimeSpan.FromSeconds(30), cancellationToken).Vhc()) {
                Console.Error.WriteLine("The server has not stopped within 30 seconds.");
                return 1;
            }

            Console.WriteLine("The server has stopped.");
            return 0;
        });
        return command;
    }

    // the running start command holds the instance lock until its process ends
    private async Task<bool> WaitForInstanceExit(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true) {
            try {
                File.OpenWrite(LockFilePath).Dispose();
                return true;
            }
            catch (IOException) when (DateTime.UtcNow < deadline) {
                // A starting server's listener clears the command file before it listens, so a stop
                // written in its first moments is sent again. No clock decides: a file a previous
                // run left is never taken for a new stop.
                if (!File.Exists(_commandListener.CommandFilePath))
                    _commandListener.SendCommand("stop");

                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).Vhc();
            }
            catch (IOException) {
                return false;
            }
        }
    }

    private Command CreateStartCommand()
    {
        var command = new Command("start", "Run the server (default command)");
        command.SetAction(async (_, _) => {

            // A signal - SIGTERM, Ctrl+C, a terminal's hang-up - stops the server as the stop command
            // does, and the process ends once it has stopped. The parser handles no signal (Start).
            _signalRegistrations = [
                PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal),
                PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal),
                PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnSignal)
            ];

            // LogAnonymizer is on by default
            VhLogger.IsAnonymousMode = AppSettings.ServerConfig?.LogAnonymizerValue ?? true;

            // find listener port
            if (IsAnotherInstanceRunning())
                throw new AnotherInstanceIsRunningException();

            // initialize logger
            _serverLog = ServerLog.Start(StoragePath, AppFolderPath);
            VhLogger.Instance.LogInformation("Settings from {AppSettingsSource}", _appSettingsSource);

            // from here a stop command ends the start-up too; only the lock's holder may listen, since
            // starting to listen clears the command file
            _commandListener.Start();

            try {
                // check FileAccessManager; the access manager's first use, now that the log exists
                if (FileAccessManager != null && await FileAccessManager.AccessTokenService.GetTotalCount() == 0)
                    VhLogger.Instance.LogWarning(
                        "There is no token in the store! Use the following command to create one:\n " +
                        "dotnet VpnHoodServer.dll gen -?");

                await RunServer().Vhc();
            }
            catch (Exception ex) {
                // the parser reports it on stderr, which an installed service discards
                VhLogger.Instance.LogError(ex, "The server could not start.");
                throw;
            }
        });

        return command;
    }

    // the signal is held, so the process stays until the start command has stopped the server
    private void OnSignal(PosixSignalContext context)
    {
        context.Cancel = true;
        VhLogger.Instance.LogInformation("I have received the {Signal} signal!", context.Signal);
        _stopCts.TryCancel();
    }

    // A signal or the stop command cancels _stopCts, which ends the server here, the only place that
    // disposes it, and then its adapter.
    private async Task RunServer()
    {
        var stopToken = _stopCts.Token;

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
        var virtualIpNetworkV4 = ServerTransportDefaults.VirtualIpNetworkV4;
        var virtualIpNetworkV6 = ServerTransportDefaults.VirtualIpNetworkV6;
        try {
            // a stop that came before: no adapter to build and tear down again
            stopToken.ThrowIfCancellationRequested();
            _vpnAdapter = await CreateTunProvider(virtualIpNetworkV4, virtualIpNetworkV6, stopToken).Vhc();
            var server = new VpnHoodServer(AccessManager, new ServerOptions {
                Tracker = _tracker,
                VpnAdapter = _vpnAdapter,
                SystemInfoProvider = systemInfoProvider,
                NetConfigurationProvider = configurationProvider,
                SwapMemoryProvider = swapMemoryProvider,
                QuicServer = MsQuicServer.IsSupported ? new MsQuicServer() : null,
                StoragePath = InternalStoragePath,
                Config = AppSettings.ServerConfig,
                DownloadsPath = _downloadsPath,
                VirtualIpNetworkV4 = virtualIpNetworkV4,
                VirtualIpNetworkV6 = virtualIpNetworkV6
            });
            _vpnHoodServer = server;

            try {
                await server.Start(stopToken).Vhc();
                await Task.Delay(Timeout.Infinite, stopToken).Vhc(); // the server never disposes itself
            }
            finally {
                await DisposeServer(server).Vhc();
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested) {
            // a stop or a signal, at any point: not a failure
        }
        finally {
            // nothing else removes the tun and its NAT and forwarding rules, and no finalizer runs at exit
            _vpnAdapter?.Dispose();
        }
    }

    // logged rather than thrown: a failed disposal must not hide a start-up failure, nor skip the adapter
    private static async Task DisposeServer(VpnHoodServer server)
    {
        try {
            await server.DisposeAsync().Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not stop the server cleanly.");
        }
    }

    private string LockFilePath => Path.Combine(InternalStoragePath, "server.lock");

    private bool IsAnotherInstanceRunning()
    {
        try {
            _lockStream = File.OpenWrite(LockFilePath);
            var stream = new StreamWriter(_lockStream, leaveOpen: true);
            stream.WriteLine(DateTime.UtcNow);
            stream.Dispose();
            return false;
        }
        catch (IOException) {
            return true;
        }
    }

    // Proxy only where there is no adapter to have: not Linux, or a failure, such as a host without
    // IPv6, whose probe the constructor runs. A stop is passed on, not taken for a failure.
    private static async Task<IVpnAdapter?> CreateTunProvider(IpNetwork virtualIpNetworkV4,
        IpNetwork virtualIpNetworkV6, CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return null;

        try {
            var vpnAdapter = new LinuxTunVpnAdapter(new LinuxVpnAdapterSettings {
                AdapterName = AppName,
                AppId = AppId,
                Blocking = false,
                AutoDisposePackets = true
            });

            try {
                VhLogger.Instance.LogInformation("Starting VpnAdapter...");
                await vpnAdapter.Start(new VpnAdapterOptions {
                    SessionName = "VpnHoodServer",
                    Mtu = TransportDefaults.MtuServer,
                    UseNat = true,
                    VirtualIpNetworkV4 = virtualIpNetworkV4,
                    VirtualIpNetworkV6 = virtualIpNetworkV6
                }, cancellationToken).Vhc();
                return vpnAdapter;
            }
            catch {
                // a half-made adapter would stay subscribed to address changes and restart itself
                vpnAdapter.Dispose();
                throw;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
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

        // the signal registrations stay to the process's end, so a late signal is still held
        _serverLog?.Dispose();
    }

    public async Task<int> Start(string[] args, CancellationToken cancellationToken)
    {
        // replace "/?"
        for (var i = 0; i < args.Length; i++)
            if (args[i] == "/?" || args[i] == "-?")
                args[i] = "--help";

        // handle --version and exit
        if (args.Any(arg => arg.Equals("--version", StringComparison.OrdinalIgnoreCase))) {
            Console.WriteLine("Version:");
            Console.WriteLine(VpnHoodServer.ServerVersion.ToString(3));
            return 0;
        }

        // set default
        if (args.Length == 0) args = ["start"];
        var startCommand = CreateStartCommand();
        var rootCommand = new RootCommand($"VpnHood! SERVER v{VpnHoodServer.ServerVersion.ToString(3)}") {
            startCommand,
            CreateStopCommand(),
            CreateGcCommand()
        };

        if (AppSettings.HttpAccessManager == null)
            new FileAccessManagerCommand(() => FileAccessManager ??
                                               throw new InvalidOperationException("The access manager is not a FileAccessManager."))
                .AddCommands(rootCommand);

        // start logs by NLog.config (ServerLog); any other command logs to the terminal, so the
        // person running it sees what its access manager finds. One command per process, never both.
        var parseResult = rootCommand.Parse(args);
        if (parseResult.CommandResult.Command != startCommand)
            VhLogger.AddProvider(new ConsoleLoggerProvider());

        // No signal handling by the parser: its handler can cancel a source it has just disposed when a
        // signal lands as a command ends, which crashes the process. start holds its own signals (its
        // action); a signal ends any other command at once.
        var invocationConfiguration = new InvocationConfiguration { ProcessTerminationTimeout = null };
        return await parseResult.InvokeAsync(invocationConfiguration, cancellationToken).Vhc();
    }
}