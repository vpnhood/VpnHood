using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Exceptions;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Server.SystemInformation;

namespace VpnHood.Server;

public class VpnHoodServer : IAsyncDisposable, IDisposable, IJob
{
    private readonly bool _autoDisposeAccessServer;
    private readonly TcpHost _tcpHost;
    private readonly string _lastConfigFilePath;
    private bool _disposed;
    private string? _lastConfigError;
    private string? _lastConfigCode;
    private readonly bool _publicIpDiscovery;
    private readonly ServerConfig? _config;
    private readonly TimeSpan _configureInterval;
    private Task _configureTask = Task.CompletedTask;
    private Task _sendStatusTask = Task.CompletedTask;
    public JobSection JobSection { get; }

    public VpnHoodServer(IAccessServer accessServer, ServerOptions options)
    {
        if (options.SocketFactory == null)
            throw new ArgumentNullException(nameof(options.SocketFactory));

        AccessServer = accessServer;
        SystemInfoProvider = options.SystemInfoProvider ?? new BasicSystemInfoProvider();
        SessionManager = new SessionManager(accessServer, options.NetFilter, options.SocketFactory, options.Tracker);
        JobSection = new JobSection(options.ConfigureInterval);

        _configureInterval = options.ConfigureInterval;
        _autoDisposeAccessServer = options.AutoDisposeAccessServer;
        _lastConfigFilePath = Path.Combine(options.StoragePath, "last-config.json");
        _publicIpDiscovery = options.PublicIpDiscovery;
        _config = options.Config;
        _tcpHost = new TcpHost(SessionManager, new SslCertificateManager(AccessServer));

        JobRunner.Default.Add(this);
    }

    public SessionManager SessionManager { get; }
    public ServerState State { get; private set; } = ServerState.NotStarted;
    public IAccessServer AccessServer { get; }
    public ISystemInfoProvider SystemInfoProvider { get; }

    private static void ConfigMinIoThreads(int? minCompletionPortThreads)
    {
        // Configure thread pool size
        ThreadPool.GetMinThreads(out var minWorkerThreads, out var newMinCompletionPortThreads);
        minCompletionPortThreads ??= newMinCompletionPortThreads * 30;
        if (minCompletionPortThreads != 0) newMinCompletionPortThreads = minCompletionPortThreads.Value;
        ThreadPool.SetMinThreads(minWorkerThreads, newMinCompletionPortThreads);
        VhLogger.Instance.LogInformation(
            "MinWorkerThreads: {MinWorkerThreads}, MinCompletionPortThreads: {newMinCompletionPortThreads}",
            minWorkerThreads, newMinCompletionPortThreads);
    }

    private static void ConfigMaxIoThreads(int? maxCompletionPortThreads)
    {
        ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var newMaxCompletionPortThreads);
        maxCompletionPortThreads ??= 0xFFFF; // We prefer all IO operations get slow together than be queued
        if (maxCompletionPortThreads != 0) newMaxCompletionPortThreads = maxCompletionPortThreads.Value;
        ThreadPool.SetMaxThreads(maxWorkerThreads, newMaxCompletionPortThreads);
        VhLogger.Instance.LogInformation(
            "MaxWorkerThreads: {MaxWorkerThreads}, MaxCompletionPortThreads: {newMaxCompletionPortThreads}",
            maxWorkerThreads, newMaxCompletionPortThreads);
    }

    public async Task RunJob()
    {
        if (_disposed) throw new ObjectDisposedException(VhLogger.FormatType(this));

        if (State == ServerState.Waiting && _configureTask.IsCompleted)
        {
            _configureTask = Configure();
            await _configureTask;
        }

        if (State == ServerState.Ready && _sendStatusTask.IsCompleted)
        {
            _sendStatusTask = SendStatusToAccessServer();
            await _sendStatusTask;
        }
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

        // Report current OS Version
        VhLogger.Instance.LogInformation($"{GetType().Assembly.GetName().FullName}");
        VhLogger.Instance.LogInformation($"OS: {SystemInfoProvider.GetSystemInfo()}");

        // Configure
        State = ServerState.Waiting;
        await RunJob();
        JobSection.Leave();
    }

    private async Task Configure()
    {
        try
        {
            State = ServerState.Configuring;
            JobSection.Interval = _configureInterval;

            // get server info
            VhLogger.Instance.LogInformation("Configuring by the Access Server...");
            var providerSystemInfo = SystemInfoProvider.GetSystemInfo();
            var serverInfo = new ServerInfo(
                environmentVersion: Environment.Version,
                version: GetType().Assembly.GetName().Version,
                privateIpAddresses: await IPAddressUtil.GetPrivateIpAddresses(),
                publicIpAddresses: _publicIpDiscovery ? await IPAddressUtil.GetPublicIpAddresses() : Array.Empty<IPAddress>(),
                status: Status
            )
            {
                MachineName = Environment.MachineName,
                OsInfo = providerSystemInfo.OsInfo,
                OsVersion = Environment.OSVersion.ToString(),
                TotalMemory = providerSystemInfo.TotalMemory,
                LogicalCoreCount = providerSystemInfo.LogicalCoreCount,
                LastError = _lastConfigError
            };

            var publicIpV4 = serverInfo.PublicIpAddresses.SingleOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
            var publicIpV6 = serverInfo.PublicIpAddresses.SingleOrDefault(x => x.AddressFamily == AddressFamily.InterNetworkV6);
            var isIpV6Supported = publicIpV6 != null || await IPAddressUtil.IsIpv6Supported();
            VhLogger.Instance.LogInformation("Public IPv4: {IPv4}, Public IPv6: {IpV6}, IsV6Supported: {IsV6Supported}",
                VhLogger.Format(publicIpV4), VhLogger.Format(publicIpV6), isIpV6Supported);

            // get configuration from access server
            VhLogger.Instance.LogTrace("Sending config request to the Access Server...");
            var serverConfig = await ReadConfig(serverInfo);
            VhLogger.Instance.LogInformation($"ServerConfig: {JsonSerializer.Serialize(serverConfig, new JsonSerializerOptions { WriteIndented = true })}");
            serverConfig.SessionOptions.TcpBufferSize = GetBestTcpBufferSize(serverInfo.TotalMemory, serverConfig.SessionOptions.TcpBufferSize);
            SessionManager.TrackingOptions = serverConfig.TrackingOptions;
            SessionManager.SessionOptions = serverConfig.SessionOptions;
            JobSection.Interval = serverConfig.UpdateStatusInterval;
            _lastConfigCode = serverConfig.ConfigCode;
            ConfigMinIoThreads(serverConfig.MinCompletionPortThreads);
            ConfigMaxIoThreads(serverConfig.MaxCompletionPortThreads);
            VhLogger.IsAnonymousMode = serverConfig.LogAnonymizer;

            // starting the listeners
            var verb = _tcpHost.IsStarted ? "Restarting" : "Starting";
            VhLogger.Instance.LogInformation($"{verb} {VhLogger.FormatType(_tcpHost)}...");
            if (_tcpHost.IsStarted) await _tcpHost.Stop();
            _tcpHost.Start(serverConfig.TcpEndPoints, isIpV6Supported && serverConfig.AllowIpV6);

            // set config status
            State = ServerState.Ready;

            _lastConfigError = null;
            VhLogger.Instance.LogInformation("Server is ready!");
        }
        catch (Exception ex)
        {
            State = ServerState.Waiting;
            _lastConfigError = ex.Message;
            VhLogger.Instance.LogError(ex, $"Could not configure server! Retrying after {JobSection.Interval.TotalSeconds} seconds.");
            await _tcpHost.Stop();
        }
    }

    private static int GetBestTcpBufferSize(long? totalMemory, int? configValue)
    {
        if (configValue > 0)
            return configValue.Value;

        if (totalMemory == null)
            return 8192;

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

            if (_config != null)
            {
                VhLogger.Instance.LogInformation($"RemoteConfig: {JsonSerializer.Serialize(serverConfig, new JsonSerializerOptions { WriteIndented = true })}");
                VhLogger.Instance.LogWarning("Remote configuration has been overwritten by the local settings.");
                _config.ConfigCode = serverConfig.ConfigCode;
                return _config;
            }

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
                    VhLogger.Instance.LogWarning("Last configuration has been loaded to report Maintenance mode.");
                    return ret;
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogInformation(ex, "Could not load last ServerConfig.");
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
                TcpConnectionCount = SessionManager.Sessions.Values.Sum(x => x.TcpChannelCount),
                UdpConnectionCount = SessionManager.Sessions.Values.Sum(x => x.UdpConnectionCount),
                ThreadCount = Process.GetCurrentProcess().Threads.Count,
                AvailableMemory = systemInfo.AvailableMemory,
                CpuUsage = systemInfo.CpuUsage,
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
            if (res.ConfigCode != _lastConfigCode || !_tcpHost.IsStarted)
            {
                VhLogger.Instance.LogInformation("Reconfiguration was requested.");
                await Configure();
            }
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not send server status.");
        }
    }

    public void Dispose()
    {
        DisposeAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        using var scope = VhLogger.Instance.BeginScope("Server");
        VhLogger.Instance.LogInformation("Shutting down...");

        // wait for configuration
        await _configureTask;
        await _sendStatusTask;
        await _tcpHost.DisposeAsync();
        await SessionManager.DisposeAsync();

        if (_autoDisposeAccessServer)
            AccessServer.Dispose();

        State = ServerState.Disposed;
        VhLogger.Instance.LogInformation("Bye Bye!");
    }
}