using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Server.SystemInformation;
using Timer = System.Timers.Timer;

namespace VpnHood.Server;

public class VpnHoodServer : IDisposable
{
    private readonly bool _autoDisposeAccessServer;
    private readonly TcpHost _tcpHost;
    private readonly string _lastConfigFilePath;
    private readonly Timer _configureTimer;
    private System.Threading.Timer? _updateStatusTimer;
    private bool _disposed;
    private string? _lastConfigError;
    private string? _lastConfigCode;

    public VpnHoodServer(IAccessServer accessServer, ServerOptions options)
    {
        if (options.SocketFactory == null) throw new ArgumentNullException(nameof(options.SocketFactory));
        _autoDisposeAccessServer = options.AutoDisposeAccessServer;
        _lastConfigFilePath = Path.Combine(options.StoragePath, "last-config.json");
        AccessServer = accessServer;
        SystemInfoProvider = options.SystemInfoProvider ?? new BasicSystemInfoProvider();
        SessionManager = new SessionManager(accessServer, options.SocketFactory, options.Tracker);
        _tcpHost = new TcpHost(SessionManager, new SslCertificateManager(AccessServer), options.SocketFactory);

        // Configure thread pool size
        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
        ThreadPool.SetMinThreads(workerThreads, completionPortThreads * 30);

        ThreadPool.GetMaxThreads(out var workerThreadsMax, out _);
        ThreadPool.SetMaxThreads(workerThreadsMax, 0xFFFF); // We prefer all IO operations get slow together than be queued

        // update timers
        _configureTimer = new Timer(options.ConfigureInterval.TotalMilliseconds) { AutoReset = false, Enabled = false };
        _configureTimer.Elapsed += OnConfigureTimerOnElapsed;
    }

    public SessionManager SessionManager { get; }
    public ServerState State { get; private set; } = ServerState.NotStarted;
    public IAccessServer AccessServer { get; }
    public ISystemInfoProvider SystemInfoProvider { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        using var scope = VhLogger.Instance.BeginScope("Server");
        VhLogger.Instance.LogInformation("Shutting down...");
        _updateStatusTimer?.Dispose();
        _configureTimer.Dispose();

        VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName(_tcpHost)}...");
        _tcpHost.Dispose();

        VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName(SessionManager)}...");
        SessionManager.Dispose();

        if (_autoDisposeAccessServer)
        {
            VhLogger.Instance.LogTrace($"Disposing {VhLogger.FormatTypeName(AccessServer)}...");
            AccessServer.Dispose();
        }

        State = ServerState.Disposed;
        VhLogger.Instance.LogInformation("Bye Bye!");
    }

    private async void OnConfigureTimerOnElapsed(object o, ElapsedEventArgs elapsedEventArgs)
    {
        await Configure();
    }

    private async void StatusTimerCallback(object state)
    {
        await SendStatusToAccessServer();
    }

    /// <summary>
    ///     Start the server
    /// </summary>
    public async Task Start()
    {
        using var scope = VhLogger.Instance.BeginScope("Server");
        if (_disposed) throw new ObjectDisposedException(nameof(VpnHoodServer));

        if (State != ServerState.NotStarted)
            throw new Exception("Server has already started!");

        State = ServerState.Starting;

        // Report current OS Version
        VhLogger.Instance.LogInformation($"{GetType().Assembly.GetName().FullName}");
        VhLogger.Instance.LogInformation($"OS: {SystemInfoProvider.GetOperatingSystemInfo()}");

        // report config
        ThreadPool.GetMinThreads(out var minWorkerThreads, out var minCompletionPortThreads);
        ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);
        VhLogger.Instance.LogInformation(
            $"MinWorkerThreads: {minWorkerThreads}, MinCompletionPortThreads: {minCompletionPortThreads}, " +
            $"MaxWorkerThreads: {maxWorkerThreads}, MaxCompletionPortThreads: {maxCompletionPortThreads}, ");

        // Configure
        State = ServerState.Configuring;
        await Configure();
    }

    private async Task Configure()
    {
        if (_tcpHost.IsDisposed)
            throw new ObjectDisposedException($"Could not configure after disposing {nameof(TcpHost)}!");

        try
        {
            // stop update status timer
            if (_updateStatusTimer != null)
                await _updateStatusTimer.DisposeAsync();
            _updateStatusTimer = null;

            // get server info
            VhLogger.Instance.LogInformation("Configuring by the Access Server...");
            var serverInfo = new ServerInfo(
                environmentVersion: Environment.Version,
                version: typeof(VpnHoodServer).Assembly.GetName().Version,
                privateIpAddresses: await IPAddressUtil.GetPrivateIpAddresses(),
                publicIpAddresses: await IPAddressUtil.GetPublicIpAddresses(),
                status: Status
            )
            {
                MachineName = Environment.MachineName,
                OsInfo = SystemInfoProvider.GetOperatingSystemInfo(),
                OsVersion = Environment.OSVersion.ToString(),
                TotalMemory = SystemInfoProvider.GetSystemInfo().TotalMemory,
                LastError = _lastConfigError
            };

            // get configuration from access server
            VhLogger.Instance.LogTrace("Sending config request to the Access Server...");
            var serverConfig = await ReadConfig(serverInfo);
            VhLogger.Instance.LogInformation($"ServerConfig: {JsonSerializer.Serialize(serverConfig)}");
            SessionManager.TrackingOptions = serverConfig.TrackingOptions;
            SessionManager.SessionOptions = serverConfig.SessionOptions;
            _tcpHost.OrgStreamReadBufferSize = serverConfig.SessionOptions.TcpBufferSize == 0 
                ? GetBestTcpBufferSize(serverInfo.TotalMemory) : serverConfig.SessionOptions.TcpBufferSize;
            _tcpHost.TunnelStreamReadBufferSize = serverConfig.SessionOptions.TcpBufferSize == 0 
                ? GetBestTcpBufferSize(serverInfo.TotalMemory) : serverConfig.SessionOptions.TcpBufferSize;
            _tcpHost.TcpTimeout = serverConfig.SessionOptions.TcpTimeout;
            _lastConfigCode = serverConfig.ConfigCode;

            // starting the listeners
            var verb = _tcpHost.IsStarted ? "Restarting" : "Starting";
            VhLogger.Instance.LogInformation($"{verb} {VhLogger.FormatTypeName(_tcpHost)}...");
            if (_tcpHost.IsStarted) await _tcpHost.Stop();
            _tcpHost.Start(serverConfig.TcpEndPoints);

            // make sure to send status after starting the service at least once
            // it is required to inform that server is successfully configured
            if (serverConfig.UpdateStatusInterval != TimeSpan.Zero)
            {
                VhLogger.Instance.LogInformation($"Set {nameof(serverConfig.UpdateStatusInterval)} to {serverConfig.UpdateStatusInterval.TotalSeconds} seconds.");
                if (_updateStatusTimer != null)
                    await _updateStatusTimer.DisposeAsync();
                _updateStatusTimer = new System.Threading.Timer(StatusTimerCallback, null, TimeSpan.Zero, serverConfig.UpdateStatusInterval);
            }

            // set config status
            State = ServerState.Started;
            _lastConfigError = null;
            VhLogger.Instance.LogInformation("Server is ready!");
        }
        catch (Exception ex)
        {
            _lastConfigError = ex.Message;
            VhLogger.Instance.LogError(ex, $"Could not configure server! Retrying after {_configureTimer.Interval / 1000} seconds.");
            await _tcpHost.Stop();

            // start configure timer
            _configureTimer.Start();
        }
    }

    private static int GetBestTcpBufferSize(long totalMemory)
    {
        var bufferSize = (long)Math.Round((double)totalMemory / 0x80000000) * 4096;
        bufferSize = Math.Max(bufferSize, 8192);
        bufferSize = Math.Min(bufferSize, 8192); //81920, it looks it doesn't have effect
        return (int)bufferSize;
    }
    
    private async Task<ServerConfig> ReadConfig(ServerInfo serverInfo)
    {
        try
        {
            var serverConfig = await AccessServer.Server_Configure(serverInfo);
            try { await File.WriteAllTextAsync(_lastConfigFilePath, JsonSerializer.Serialize(serverConfig)); }
            catch { /* Ignore */ }
            return serverConfig;
        }
        catch (MaintenanceException)
        {
            // try to load last config
            try
            {
                if (File.Exists(_lastConfigFilePath))
                {
                    var ret = Util.JsonDeserialize<ServerConfig>(await File.ReadAllTextAsync(_lastConfigFilePath));
                    VhLogger.Instance.LogWarning("Last configuration has been loaded to report Maintenance mode!");
                    return ret;
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogInformation($"Could not load last {nameof(ServerConfig)}! Error: {ex.Message}");
            }

            throw;
        }
    }

    public ServerStatus Status
    {
        get
        {
            var systemInfo = SystemInfoProvider.GetSystemInfo();
            var serverStatus = new ServerStatus
            {
                SessionCount = SessionManager.Sessions.Count(x => !x.Value.IsDisposed),
                TcpConnectionCount = SessionManager.Sessions.Values.Sum(x => x.TcpConnectionCount),
                UdpConnectionCount = SessionManager.Sessions.Values.Sum(x => x.UdpConnectionCount),
                ThreadCount = Process.GetCurrentProcess().Threads.Count,
                FreeMemory = systemInfo.FreeMemory,
                UsedMemory = Process.GetCurrentProcess().WorkingSet64,
                TunnelSendSpeed = SessionManager.Sessions.Sum(x => x.Value.Tunnel.SendSpeed),
                TunnelReceiveSpeed = SessionManager.Sessions.Sum(x => x.Value.Tunnel.ReceiveSpeed),
                ConfigCode = _lastConfigCode
            };
            return serverStatus;
        }
    }

    private async Task SendStatusToAccessServer()
    {
        try
        {
            VhLogger.Instance.LogTrace("Sending status to Access...");
            var res = await AccessServer.Server_UpdateStatus(Status);

            // reconfigure
            if (res.ConfigCode != _lastConfigCode)
            {
                VhLogger.Instance.LogInformation("Reconfiguration was requested.");
                _ = Configure();
            }
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not send server status.");
        }
    }
}