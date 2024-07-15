using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Common.ApiClients;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Trackers;
using VpnHood.Common.Utils;
using VpnHood.Server.Access;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Managers;
using VpnHood.Server.SystemInformation;
using VpnHood.Server.Utils;
using VpnHood.Tunneling;

namespace VpnHood.Server;

public class VpnHoodServer : IAsyncDisposable, IJob
{
    private readonly bool _autoDisposeAccessManager;
    private readonly string _lastConfigFilePath;
    private bool _disposed;
    private ApiError? _lastConfigError;
    private string? _lastConfigCode;
    private readonly bool _publicIpDiscovery;
    private readonly ServerConfig? _config;
    private Task _configureTask = Task.CompletedTask;
    private Task _sendStatusTask = Task.CompletedTask;
    private Http01ChallengeService? _http01ChallengeService;

    public ServerHost ServerHost { get; }
    public JobSection JobSection { get; }
    public static Version ServerVersion => typeof(VpnHoodServer).Assembly.GetName().Version;
    public SessionManager SessionManager { get; }
    public ServerState State { get; private set; } = ServerState.NotStarted;
    public IAccessManager AccessManager { get; }
    public ISystemInfoProvider SystemInfoProvider { get; }

    public VpnHoodServer(IAccessManager accessManager, ServerOptions options)
    {
        if (options.SocketFactory == null)
            throw new ArgumentNullException(nameof(options.SocketFactory));

        AccessManager = accessManager;
        SystemInfoProvider = options.SystemInfoProvider ?? new BasicSystemInfoProvider();
        JobSection = new JobSection(options.ConfigureInterval);
        SessionManager = new SessionManager(accessManager,
            options.NetFilter,
            options.SocketFactory,
            options.Tracker,
            ServerVersion,
            new SessionManagerOptions { CleanupInterval = options.CleanupInterval });

        _autoDisposeAccessManager = options.AutoDisposeAccessManager;
        _lastConfigFilePath = Path.Combine(options.StoragePath, "last-config.json");
        _publicIpDiscovery = options.PublicIpDiscovery;
        _config = options.Config;
        ServerHost = new ServerHost(SessionManager);

        VhLogger.TcpCloseEventId = GeneralEventId.TcpLife;
        JobRunner.Default.Add(this);
    }

    public async Task RunJob()
    {
        if (_disposed) throw new ObjectDisposedException(VhLogger.FormatType(this));

        if (State == ServerState.Waiting && _configureTask.IsCompleted)
        {
            _configureTask = Configure(); // configure does not throw any error
            await _configureTask.VhConfigureAwait();
            return;
        }

        if (State == ServerState.Ready && _sendStatusTask.IsCompleted)
        {
            _sendStatusTask = SendStatusToAccessManager(true);
            await _sendStatusTask.VhConfigureAwait();
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
        VhLogger.Instance.LogInformation("Module: {Module}", GetType().Assembly.GetName().FullName);
        VhLogger.Instance.LogInformation("OS: {OS}", SystemInfoProvider.GetSystemInfo());
        VhLogger.Instance.LogInformation("IsDiagnoseMode: {IsDiagnoseMode}", VhLogger.IsDiagnoseMode);

        // Report TcpBuffers
        var tcpClient = new TcpClient();
        VhLogger.Instance.LogInformation("DefaultTcpKernelSentBufferSize: {DefaultTcpKernelSentBufferSize}, DefaultTcpKernelReceiveBufferSize: {DefaultTcpKernelReceiveBufferSize}",
            tcpClient.SendBufferSize, tcpClient.ReceiveBufferSize);

        // Report Anonymous info
        _ = GaTrackStart();

        // Configure
        State = ServerState.Waiting;
        await RunJob().VhConfigureAwait();
    }

    private async Task Configure()
    {
        try
        {
            VhLogger.Instance.LogInformation("Configuring by the Access Manager...");
            State = ServerState.Configuring;

            // get server info
            VhLogger.Instance.LogTrace("Finding free EndPoints...");
            var freeUdpPortV4 = ServerUtil.GetFreeUdpPort(AddressFamily.InterNetwork, null);
            var freeUdpPortV6 = ServerUtil.GetFreeUdpPort(AddressFamily.InterNetworkV6, freeUdpPortV4);

            VhLogger.Instance.LogTrace("Finding public addresses...");
            var privateIpAddresses = await IPAddressUtil.GetPrivateIpAddresses().VhConfigureAwait();
            
            VhLogger.Instance.LogTrace("Finding public addresses..., PublicIpDiscovery: {PublicIpDiscovery}", _publicIpDiscovery);
            var publicIpAddresses = _publicIpDiscovery ? await IPAddressUtil.GetPublicIpAddresses().VhConfigureAwait() : [];

            var providerSystemInfo = SystemInfoProvider.GetSystemInfo();
            var serverInfo = new ServerInfo {
                EnvironmentVersion = Environment.Version,
                Version = ServerVersion,
                PrivateIpAddresses = privateIpAddresses,
                PublicIpAddresses = publicIpAddresses,
                Status = GetStatus(),
                MachineName = Environment.MachineName,
                OsInfo = providerSystemInfo.OsInfo,
                OsVersion = Environment.OSVersion.ToString(),
                TotalMemory = providerSystemInfo.TotalMemory,
                LogicalCoreCount = providerSystemInfo.LogicalCoreCount,
                FreeUdpPortV4 = freeUdpPortV4,
                FreeUdpPortV6 = freeUdpPortV6
            };
            
            var publicIpV4 = serverInfo.PublicIpAddresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
            var publicIpV6 = serverInfo.PublicIpAddresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetworkV6);
            var isIpV6Supported = publicIpV6 != null || await IPAddressUtil.IsIpv6Supported().VhConfigureAwait();
            VhLogger.Instance.LogInformation("Public IPv4: {IPv4}, Public IPv6: {IpV6}, IsV6Supported: {IsV6Supported}",
                VhLogger.Format(publicIpV4), VhLogger.Format(publicIpV6), isIpV6Supported);

            // get configuration from access server
            VhLogger.Instance.LogTrace("Sending config request to the Access Server...");
            var serverConfig = await ReadConfig(serverInfo).VhConfigureAwait();
            VhLogger.IsAnonymousMode = serverConfig.LogAnonymizerValue;
            SessionManager.TrackingOptions = serverConfig.TrackingOptions;
            SessionManager.SessionOptions = serverConfig.SessionOptions;
            SessionManager.ServerSecret = serverConfig.ServerSecret ?? SessionManager.ServerSecret;
            JobSection.Interval = serverConfig.UpdateStatusIntervalValue;
            ServerUtil.ConfigMinIoThreads(serverConfig.MinCompletionPortThreads);
            ServerUtil.ConfigMaxIoThreads(serverConfig.MaxCompletionPortThreads);
            var allServerIps = serverInfo.PublicIpAddresses
                .Concat(serverInfo.PrivateIpAddresses)
                .Concat(serverConfig.TcpEndPoints?.Select(x => x.Address) ?? Array.Empty<IPAddress>());

            ConfigNetFilter(SessionManager.NetFilter, ServerHost, serverConfig.NetFilterOptions,
                privateAddresses: allServerIps, isIpV6Supported, dnsServers: serverConfig.DnsServersValue);

            // Reconfigure server host
            await ServerHost.Configure(
                    serverConfig.TcpEndPointsValue, serverConfig.UdpEndPointsValue,
                    serverConfig.DnsServersValue, serverConfig.Certificates.Select(x => new X509Certificate2(x.RawData))
                    .ToArray()).VhConfigureAwait();

            // Reconfigure dns challenge
            StartDnsChallenge(serverConfig.TcpEndPointsValue.Select(x => x.Address), serverConfig.DnsChallenge);

            // set config status
            _lastConfigCode = serverConfig.ConfigCode;
            State = ServerState.Ready;

            _lastConfigError = null;
            VhLogger.Instance.LogInformation("Server is ready!");

            // set status after successful configuration
            await SendStatusToAccessManager(false).VhConfigureAwait();
        }
        catch (Exception ex)
        {
            State = ServerState.Waiting;
            _lastConfigError = new ApiError(ex);
            if (ex is SocketException socketException)
                _lastConfigError.Data.Add("SocketErrorCode", socketException.SocketErrorCode.ToString());

            SessionManager.Tracker?.VhTrackErrorAsync(ex, "Could not configure server!", "Configure");
            VhLogger.Instance.LogError(ex, "Could not configure server! Retrying after {TotalSeconds} seconds.", JobSection.Interval.TotalSeconds);
            await SendStatusToAccessManager(false).VhConfigureAwait();
        }
    }

    private void StartDnsChallenge(IEnumerable<IPAddress> ipAddresses, DnsChallenge? dnsChallenge)
    {
        _http01ChallengeService?.Dispose();
        _http01ChallengeService = null;
        if (dnsChallenge == null)
            return;

        try
        {
            _http01ChallengeService = new Http01ChallengeService(ipAddresses.ToArray(), dnsChallenge.Token, dnsChallenge.KeyAuthorization, dnsChallenge.Timeout);
            _http01ChallengeService.Start();
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(GeneralEventId.DnsChallenge, ex, "Could not start the Http01ChallengeService.");
        }
    }

    private static void ConfigNetFilter(INetFilter netFilter, ServerHost serverHost, NetFilterOptions netFilterOptions,
        IEnumerable<IPAddress> privateAddresses, bool isIpV6Supported, IEnumerable<IPAddress> dnsServers)
    {
        var dnsServerIpRanges = dnsServers.Select(x => new IpRange(x)).ToOrderedList();

        // assign to workers
        serverHost.NetFilterIncludeIpRanges = netFilterOptions.GetFinalIncludeIpRanges().Union(dnsServerIpRanges).ToArray();
        serverHost.NetFilterPacketCaptureIncludeIpRanges = netFilterOptions.GetFinalPacketCaptureIncludeIpRanges().Union(dnsServerIpRanges).ToArray();
        serverHost.IsIpV6Supported = isIpV6Supported && !netFilterOptions.BlockIpV6Value;
        netFilter.BlockedIpRanges = netFilterOptions.GetBlockedIpRanges().Exclude(dnsServerIpRanges);

        // exclude listening ip
        if (!netFilterOptions.IncludeLocalNetworkValue)
            netFilter.BlockedIpRanges = netFilter.BlockedIpRanges.Union(privateAddresses.Select(x => new IpRange(x)));
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
        var serverConfig = await ReadConfigImpl(serverInfo).VhConfigureAwait();
        serverConfig.SessionOptions.TcpBufferSize = GetBestTcpBufferSize(serverInfo.TotalMemory, serverConfig.SessionOptions.TcpBufferSize);
        serverConfig.ApplyDefaults();
        VhLogger.Instance.LogInformation("RemoteConfig: {RemoteConfig}", GetServerConfigReport(serverConfig));

        if (_config != null)
        {
            _config.ConfigCode = serverConfig.ConfigCode;
            serverConfig.Merge(_config);
            VhLogger.Instance.LogWarning("Remote configuration has been overwritten by the local settings.");
            VhLogger.Instance.LogInformation("RemoteConfig: {RemoteConfig}", GetServerConfigReport(serverConfig));
        }

        // override defaults
        return serverConfig;
    }

    private static string GetServerConfigReport(ServerConfig serverConfig)
    {
        var json = JsonSerializer.Serialize(serverConfig, new JsonSerializerOptions { WriteIndented = true });
        return VhLogger.IsAnonymousMode
            ? VhUtil.RedactJsonValue(json, [
                nameof(ServerConfig.ServerSecret),
                nameof(CertificateData.RawData),
                nameof(ServerConfig.TcpEndPoints),
                nameof(ServerConfig.UdpEndPoints)
            ])
            : JsonSerializer.Serialize(serverConfig, new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task<ServerConfig> ReadConfigImpl(ServerInfo serverInfo)
    {
        try
        {
            var serverConfig = await AccessManager.Server_Configure(serverInfo).VhConfigureAwait();
            try { await File.WriteAllTextAsync(_lastConfigFilePath, JsonSerializer.Serialize(serverConfig)).VhConfigureAwait(); }
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
                    var configJson = await File.ReadAllTextAsync(_lastConfigFilePath).VhConfigureAwait();
                    var ret = VhUtil.JsonDeserialize<ServerConfig>(configJson);
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

    public ServerStatus GetStatus()
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
            TunnelSpeed = new Traffic
            {
                Sent = SessionManager.Sessions.Sum(x => x.Value.Tunnel.Speed.Sent),
                Received = SessionManager.Sessions.Sum(x => x.Value.Tunnel.Speed.Received)
            },
            ConfigCode = _lastConfigCode,
            ConfigError = _lastConfigError?.ToJson()
        };
        return serverStatus;
    }

    private async Task SendStatusToAccessManager(bool allowConfigure)
    {
        try
        {
            var status = GetStatus();
            VhLogger.Instance.LogTrace("Sending status to Access... ConfigCode: {ConfigCode}", status.ConfigCode);
            var res = await AccessManager.Server_UpdateStatus(status).VhConfigureAwait();

            // reconfigure
            if (allowConfigure && (res.ConfigCode != _lastConfigCode))
            {
                VhLogger.Instance.LogInformation("Reconfiguration was requested.");
                await Configure().VhConfigureAwait();
            }
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not send the server status.");
        }
    }

    private Task GaTrackStart()
    {
        if (SessionManager.Tracker == null)
            return Task.CompletedTask;

        // track
        return SessionManager.Tracker.Track(new TrackEvent
        {
            EventName = TrackEventNames.SessionStart,
            Parameters = new Dictionary<string, object>
            {
                { "access_manager", AccessManager.GetType().Name }
            }
        });
    }

    public void Dispose()
    {
        DisposeAsync()
            .VhConfigureAwait()
            .GetAwaiter()
            .GetResult();
    }

    private readonly AsyncLock _disposeLock = new();
    public async ValueTask DisposeAsync()
    {
        using var lockResult = await _disposeLock.LockAsync().VhConfigureAwait();
        if (_disposed) return;
        _disposed = true;

        using var scope = VhLogger.Instance.BeginScope("Server");
        VhLogger.Instance.LogInformation("Shutting down...");

        // wait for configuration
        try { await _configureTask.VhConfigureAwait(); } catch {/* no error */ }
        try { await _sendStatusTask.VhConfigureAwait(); } catch {/* no error*/ }
        await ServerHost.DisposeAsync().VhConfigureAwait(); // before disposing session manager to prevent recovering sessions
        await SessionManager.DisposeAsync().VhConfigureAwait();
        _http01ChallengeService?.Dispose();

        if (_autoDisposeAccessManager)
            AccessManager.Dispose();

        State = ServerState.Disposed;
        VhLogger.Instance.LogInformation("Bye Bye!");
    }
}