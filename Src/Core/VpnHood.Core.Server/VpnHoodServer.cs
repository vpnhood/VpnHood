using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Trackers;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.SystemInformation;
using VpnHood.Core.Server.Utils;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;

namespace VpnHood.Core.Server;

public class VpnHoodServer : IAsyncDisposable
{
    private readonly bool _autoDisposeAccessManager;
    private readonly string _lastConfigFilePath;
    private bool _disposed;
    private Exception? _lastConfigException;
    private string? _lastConfigCode;
    private readonly bool _publicIpDiscovery;
    private readonly ServerConfig? _config;
    private Task _configureTask = Task.CompletedTask;
    private Task _sendStatusTask = Task.CompletedTask;
    private Http01ChallengeService? _http01ChallengeService;
    private readonly NetConfigurationService? _netConfigurationService;
    private readonly ISystemInfoProvider _systemInfoProvider;
    private readonly ISwapMemoryProvider? _swapMemoryProvider;
    private string? _tcpCongestionControl;
    private bool _isRestarted = true;
    private readonly Job _configureAndSendStatusJob;

    public ServerHost ServerHost { get; }
    public static Version ServerVersion => typeof(VpnHoodServer).Assembly.GetName().Version ?? new Version();
    public SessionManager SessionManager { get; }
    public ServerState State { get; private set; } = ServerState.NotStarted;
    public IAccessManager AccessManager { get; }

    public VpnHoodServer(IAccessManager accessManager, ServerOptions options)
    {
        if (options.SocketFactory == null)
            throw new ArgumentNullException(nameof(options.SocketFactory));

        if (options.VpnAdapter is { IsNatSupported: false })
            throw new InvalidProgramException("VpnAdapter must support NAT to work with VpnServer.");

        AccessManager = accessManager;
        _systemInfoProvider = options.SystemInfoProvider ?? new BasicSystemInfoProvider();
        SessionManager = new SessionManager(accessManager,
            options.NetFilter,
            options.SocketFactory,
            options.Tracker,
            vpnAdapter: options.VpnAdapter,
            serverVersion: ServerVersion,
            storagePath: options.StoragePath,
            new SessionManagerOptions {
                DeadSessionTimeout = options.DeadSessionTimeout,
                HeartbeatInterval = options.HeartbeatInterval,
                VirtualIpNetworkV4 = options.VirtualIpNetworkV4,
                VirtualIpNetworkV6 = options.VirtualIpNetworkV6
            });

        _autoDisposeAccessManager = options.AutoDisposeAccessManager;
        _lastConfigFilePath = Path.Combine(options.StoragePath, "last-config.json");
        _publicIpDiscovery = options.PublicIpDiscovery;
        _netConfigurationService = options.NetConfigurationProvider != null
            ? new NetConfigurationService(options.NetConfigurationProvider)
            : null;
        _swapMemoryProvider = options.SwapMemoryProvider;
        _config = options.Config;
        ServerHost = new ServerHost(SessionManager);

        VhLogger.TcpCloseEventId = GeneralEventId.TcpLife;
        _configureAndSendStatusJob = new Job(ConfigureAndSendStatus, options.ConfigureInterval, nameof(VpnHoodServer));
    }

    public async ValueTask ConfigureAndSendStatus(CancellationToken cancellationToken)
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        using var scope = VhLogger.Instance.BeginScope("Server");

        if (State == ServerState.Waiting && _configureTask.IsCompleted) {
            _configureTask = Configure(); // configure does not throw any error
            await _configureTask.Vhc();
            return;
        }

        if (State == ServerState.Ready && _sendStatusTask.IsCompleted) {
            _sendStatusTask = SendStatusToAccessManager(true);
            await _sendStatusTask.Vhc();
        }
    }

    public async Task Start()
    {
        using var scope = VhLogger.Instance.BeginScope("Server");
        if (_disposed) throw new ObjectDisposedException(nameof(VpnHoodServer));
        if (State != ServerState.NotStarted)
            throw new Exception("Server has already started!");

        // Report current OS Version
        VhLogger.Instance.LogInformation("Module: {Module}", GetType().Assembly.GetName().FullName);
        VhLogger.Instance.LogInformation("OS: {OS}", _systemInfoProvider.GetSystemInfo());
        VhLogger.Instance.LogInformation("VirtualNetworkV4: {VirtualIpV4}, VirtualNetworkV6: {VirtualIpV6}",
            SessionManager.VirtualIpNetworkV4, SessionManager.VirtualIpNetworkV6);
        VhLogger.Instance.LogInformation("MinLogLevel: {MinLogLevel}", VhLogger.MinLogLevel);

        // Report TcpBuffers
        var tcpClient = new TcpClient();
        VhLogger.Instance.LogInformation(
            "DefaultTcpKernelSentBufferSize: {DefaultTcpKernelSentBufferSize}, DefaultTcpKernelReceiveBufferSize: {DefaultTcpKernelReceiveBufferSize}",
            tcpClient.SendBufferSize, tcpClient.ReceiveBufferSize);

        // Report Anonymous info
        _ = SessionManager.Tracker?.TryTrack(new TrackEvent {
            EventName = TrackEventNames.SessionStart,
            Parameters = new Dictionary<string, object> {
                { "access_manager", AccessManager.GetType().Name }
            }
        });


        // Configure
        State = ServerState.Waiting;

        // recover previous sessions
        try {
            VhLogger.Instance.LogInformation("Recovering the old sessions...");
            await SessionManager.RecoverSessions();
            VhLogger.Instance.LogInformation("The old sessions have been recovered. SessionCount: {SessionCount}",
                SessionManager.Sessions.Count);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not recover old sessions.");
        }

        // ReSharper disable once DisposeOnUsingVariable
        scope?.Dispose();
        await _configureAndSendStatusJob.RunNow();
    }

    private async Task Configure()
    {
        try {
            VhLogger.Instance.LogInformation("Configuring by the Access Manager...");
            State = ServerState.Configuring;

            // get server info
            VhLogger.Instance.LogDebug("Finding free EndPoints...");
            var freeUdpPortV4 = ServerUtil.GetFreeUdpPort(AddressFamily.InterNetwork, null);
            var freeUdpPortV6 = ServerUtil.GetFreeUdpPort(AddressFamily.InterNetworkV6, freeUdpPortV4);

            VhLogger.Instance.LogDebug("Finding public addresses...");
            var privateIpAddresses = await IPAddressUtil.GetPrivateIpAddresses().Vhc();

            VhLogger.Instance.LogDebug("Finding public addresses..., PublicIpDiscovery: {PublicIpDiscovery}",
                _publicIpDiscovery);
            var publicIpAddresses = _publicIpDiscovery
                ? await IPAddressUtil.GetPublicIpAddresses(CancellationToken.None).Vhc()
                : [];

            var providerSystemInfo = _systemInfoProvider.GetSystemInfo();
            var serverInfo = new ServerInfo {
                EnvironmentVersion = Environment.Version,
                Version = ServerVersion,
                PrivateIpAddresses = privateIpAddresses,
                PublicIpAddresses = publicIpAddresses,
                Status = await GetStatus(),
                MachineName = Environment.MachineName,
                OsInfo = providerSystemInfo.OsInfo,
                OsVersion = Environment.OSVersion.ToString(),
                TotalMemory = providerSystemInfo.TotalMemory,
                LogicalCoreCount = providerSystemInfo.LogicalCoreCount,
                FreeUdpPortV4 = freeUdpPortV4,
                FreeUdpPortV6 = freeUdpPortV6,
                IsRestarted = _isRestarted,
                NetworkInterfaceNames = _netConfigurationService != null
                    ? await _netConfigurationService.GetNetworkInterfaceNames()
                    : null
            };

            VhLogger.Instance.LogInformation("ServerInfo: {ServerInfo}", GetServerInfoReport(serverInfo));

            // get configuration from access server
            VhLogger.Instance.LogDebug("Sending config request to the Access Server...");
            var serverConfig = await ReadConfig(serverInfo).Vhc();
            VhLogger.IsAnonymousMode = serverConfig.LogAnonymizerValue;
            SessionManager.TrackingOptions = serverConfig.TrackingOptions;
            SessionManager.SessionOptions = serverConfig.SessionOptions;
            SessionManager.ServerSecret = serverConfig.ServerSecret ?? SessionManager.ServerSecret;
            _configureAndSendStatusJob.Period = serverConfig.UpdateStatusIntervalValue < serverConfig.SessionOptions.SyncIntervalValue
                ? serverConfig.UpdateStatusIntervalValue // update status interval must be less than sync interval
                : serverConfig.SessionOptions.SyncIntervalValue;

            ServerUtil.ConfigMinIoThreads(serverConfig.MinCompletionPortThreads);
            ServerUtil.ConfigMaxIoThreads(serverConfig.MaxCompletionPortThreads);
            var allServerIps = serverInfo.PublicIpAddresses
                .Concat(serverInfo.PrivateIpAddresses)
                .Concat(serverConfig.TcpEndPoints?.Select(x => x.Address) ?? []);

            ConfigNetFilter(SessionManager.NetFilter, ServerHost, serverConfig.NetFilterOptions,
                privateAddresses: allServerIps,
                isIpV6Supported: serverInfo.PublicIpAddresses.Any(x => x.IsV6()),
                dnsServers: serverConfig.DnsServersValue,
                virtualIpNetworkV4: SessionManager.VirtualIpNetworkV4,
                virtualIpNetworkV6: SessionManager.VirtualIpNetworkV6);

            // Add listener ip addresses to the ip address manager if requested
            if (_netConfigurationService != null) {
                foreach (var ipEndPoint in serverConfig.TcpEndPointsValue)
                    await _netConfigurationService
                        .AddIpAddress(ipEndPoint.Address, serverConfig.AddListenerIpsToNetwork).Vhc();
            }

            // Set TcpCongestionControl
            if (_netConfigurationService != null && !string.IsNullOrEmpty(serverConfig.TcpCongestionControlValue)) {
                await _netConfigurationService.SetTcpCongestionControl(serverConfig.TcpCongestionControlValue)
                    .Vhc();
            }

            _tcpCongestionControl = _netConfigurationService != null
                ? await _netConfigurationService.GetTcpCongestionControl()
                : null;

            if (_swapMemoryProvider != null) {
                await ConfigureSwapMemory(serverConfig.SwapMemorySizeMb);
            }

            // Reconfigure server host
            await ServerHost.Configure(new ServerHostConfiguration {
                DnsServers = serverConfig.DnsServersValue,
                TcpEndPoints = serverConfig.TcpEndPointsValue,
                UdpEndPoints = serverConfig.UdpEndPointsValue,
                Certificates = serverConfig.Certificates.Select(x => new X509Certificate2(x.RawData)).ToArray(),
                UdpReceiveBufferSize = serverConfig.SessionOptions.UdpReceiveBufferSizeValue,
                UdpSendBufferSize = serverConfig.SessionOptions.UdpSendBufferSizeValue,
            }).Vhc();

            // Reconfigure dns challenge
            StartDnsChallenge(serverConfig.TcpEndPointsValue.Select(x => x.Address), serverConfig.DnsChallenge);

            // set config status
            _lastConfigCode = serverConfig.ConfigCode;
            State = ServerState.Ready;

            _lastConfigException = null;
            VhLogger.Instance.LogInformation("Server is ready!");

            // set status after successful configuration
            await SendStatusToAccessManager(false).Vhc();
        }
        catch (Exception ex) {
            State = ServerState.Waiting;
            _lastConfigException = ex;
            SessionManager.Tracker?.TryTrackError(ex, "Could not configure server!", "Configure");
            VhLogger.Instance.LogError(ex, "Could not configure server! Retrying after {TotalSeconds} seconds.",
                _configureAndSendStatusJob.Period.TotalSeconds);
            await SendStatusToAccessManager(false).Vhc();
        }
    }

    private async Task<SwapMemoryInfo?> TryGetSwapMemoryInfo()
    {
        if (_swapMemoryProvider == null)
            return null;

        try {
            return await _swapMemoryProvider.GetInfo();
        }
        catch {
            return null;
        }
    }

    private async Task ConfigureSwapMemory(long? sizeMb)
    {
        if (_swapMemoryProvider == null)
            throw new InvalidOperationException("SwapMemoryProvider is not available.");

        try {
            var info = await _swapMemoryProvider.GetInfo();
            var size = sizeMb * VhUtils.Megabytes ?? 0;
            var otherSize = info.TotalSize - info.AppSize;
            var newAppSize = Math.Max(0, size - otherSize);

            if (Math.Abs(info.AppSize - newAppSize) < VhUtils.Megabytes) {
                if (size == 0 && info.AppSize == 0)
                    return; // there is no need to configure swap file

                VhLogger.Instance.LogInformation(
                    "Swap file size is already near to the requested size. CurrentSize: {CurrentSize}, RequestedSize: {RequestedSize}",
                    VhUtils.FormatBytes(info.TotalSize), VhUtils.FormatBytes(size));
                return;
            }

            await _swapMemoryProvider.SetAppSwapMemorySize(newAppSize);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not configure swap file.");
        }
    }

    private void StartDnsChallenge(IEnumerable<IPAddress> ipAddresses, DnsChallenge? dnsChallenge)
    {
        _http01ChallengeService?.Dispose();
        _http01ChallengeService = null;
        if (dnsChallenge == null)
            return;

        try {
            _http01ChallengeService = new Http01ChallengeService(ipAddresses.ToArray(), dnsChallenge.Token,
                dnsChallenge.KeyAuthorization, dnsChallenge.Timeout);
            _http01ChallengeService.Start();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.DnsChallenge, ex, "Could not start the Http01ChallengeService.");
        }
    }

    private static void ConfigNetFilter(INetFilter netFilter, ServerHost serverHost, NetFilterOptions netFilterOptions,
        IEnumerable<IPAddress> privateAddresses, bool isIpV6Supported, IEnumerable<IPAddress> dnsServers,
        IpNetwork virtualIpNetworkV4, IpNetwork virtualIpNetworkV6)
    {
        var dnsServerIpRanges = dnsServers.Select(x => new IpRange(x)).ToOrderedList();

        // assign to workers
        serverHost.NetFilterIncludeIpRanges = netFilterOptions
            .GetFinalIncludeIpRanges()
            .Union(dnsServerIpRanges)
            .ToArray();

        serverHost.NetFilterVpnAdapterIncludeIpRanges = netFilterOptions
            .GetFinalVpnAdapterIncludeIpRanges()
            .Union(dnsServerIpRanges)
            .ToArray();

        serverHost.IsIpV6Supported = isIpV6Supported && !netFilterOptions.BlockIpV6Value;
        netFilter.BlockedIpRanges = netFilterOptions.GetBlockedIpRanges().Exclude(dnsServerIpRanges);

        // exclude virtual ip if network isolation is enabled
        if (netFilterOptions.NetworkIsolationValue) {
            netFilter.BlockedIpRanges = netFilter.BlockedIpRanges
                .Union(virtualIpNetworkV4.ToIpRange())
                .Union(virtualIpNetworkV6.ToIpRange());
        }

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
        var serverConfig = await ReadConfigImpl(serverInfo).Vhc();
        serverConfig.SessionOptions.TcpBufferSize =
            GetBestTcpBufferSize(serverInfo.TotalMemory, serverConfig.SessionOptions.TcpBufferSize);
        serverConfig.ApplyDefaults();
        VhLogger.Instance.LogInformation("RemoteConfig: {RemoteConfig}", GetServerConfigReport(serverConfig));

        if (_config != null) {
            _config.ConfigCode = serverConfig.ConfigCode;
            serverConfig.Merge(_config);
            VhLogger.Instance.LogWarning("Remote configuration has been overwritten by the local settings.");
            VhLogger.Instance.LogInformation("RemoteConfig: {RemoteConfig}", GetServerConfigReport(serverConfig));
        }

        // override defaults
        return serverConfig;
    }

    private static string GetServerInfoReport(ServerInfo serverInfo)
    {
        var json = JsonSerializer.Serialize(serverInfo, new JsonSerializerOptions { WriteIndented = true });
        return VhLogger.IsAnonymousMode
            ? JsonUtils.RedactValue(json, [
                nameof(ServerInfo.PrivateIpAddresses),
                nameof(ServerInfo.PublicIpAddresses)
            ])
            : json;
    }

    private static string GetServerConfigReport(ServerConfig serverConfig)
    {
        var json = JsonSerializer.Serialize(serverConfig, new JsonSerializerOptions { WriteIndented = true });
        return VhLogger.IsAnonymousMode
            ? JsonUtils.RedactValue(json, [
                nameof(ServerConfig.ServerSecret),
                nameof(CertificateData.RawData),
                nameof(ServerConfig.TcpEndPoints),
                nameof(ServerConfig.UdpEndPoints)
            ])
            : JsonUtils.RedactValue(json, [
                nameof(CertificateData.RawData) // it will ruin the report and useless to see
            ]);
    }

    private async Task<ServerConfig> ReadConfigImpl(ServerInfo serverInfo)
    {
        try {
            var serverConfig = await AccessManager.Server_Configure(serverInfo).Vhc();
            _isRestarted = false; // is restarted should send once
            try {
                await File.WriteAllTextAsync(_lastConfigFilePath, JsonSerializer.Serialize(serverConfig))
                    .Vhc();
            }
            catch {
                /* Ignore */
            }

            return serverConfig;
        }
        catch (MaintenanceException) {
            // try to load last config
            try {
                if (File.Exists(_lastConfigFilePath)) {
                    var configJson = await File.ReadAllTextAsync(_lastConfigFilePath).Vhc();
                    var ret = JsonUtils.Deserialize<ServerConfig>(configJson);
                    VhLogger.Instance.LogWarning("Last configuration has been loaded to report Maintenance mode.");
                    return ret;
                }
            }
            catch (Exception ex) {
                VhLogger.Instance.LogInformation(ex, "Could not load last ServerConfig.");
            }

            throw;
        }
    }

    public async Task<ServerStatus> GetStatus()
    {
        var swapMemoryInfo = await TryGetSwapMemoryInfo();
        var systemInfo = _systemInfoProvider.GetSystemInfo();
        var serverStatus = new ServerStatus {
            SessionCount = SessionManager.Sessions.Count(x => !x.Value.IsDisposed),
            TcpConnectionCount = SessionManager.Sessions.Values.Sum(x => x.TcpChannelCount),
            UdpConnectionCount = SessionManager.Sessions.Values.Sum(x => x.UdpConnectionCount),
            ThreadCount = Process.GetCurrentProcess().Threads.Count,
            AvailableMemory = systemInfo.AvailableMemory,
            TotalSwapMemory = swapMemoryInfo?.TotalSize,
            AvailableSwapMemory = swapMemoryInfo != null ? swapMemoryInfo.TotalSize - swapMemoryInfo.TotalUsed : null,
            TcpCongestionControl = _tcpCongestionControl,
            CpuUsage = systemInfo.CpuUsage,
            UsedMemory = Process.GetCurrentProcess().WorkingSet64,
            TunnelSpeed = new Traffic {
                Sent = SessionManager.Sessions.Sum(x => x.Value.Tunnel.Speed.Sent),
                Received = SessionManager.Sessions.Sum(x => x.Value.Tunnel.Speed.Received)
            },
            ConfigCode = _lastConfigCode,
            ConfigError = _lastConfigException?.ToApiError().ToJson()
        };
        return serverStatus;
    }

    private async Task SendStatusToAccessManager(bool allowConfigure)
    {
        try {
            var status = await GetStatus();
            VhLogger.Instance.LogDebug("Sending status to Access... ConfigCode: {ConfigCode}", status.ConfigCode);
            status.SessionUsages = SessionManager.CollectSessionUsages();
            var res = await AccessManager.Server_UpdateStatus(status).Vhc();
            SessionManager.ApplySessionResponses(res.SessionResponses);

            // reconfigure
            if (allowConfigure && res.ConfigCode != _lastConfigCode) {
                VhLogger.Instance.LogInformation("Reconfiguration was requested.");
                await Configure().Vhc();
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not send the server status.");
        }
    }

    public void Dispose()
    {
        DisposeAsync()
            .Vhc()
            .GetAwaiter()
            .GetResult();
    }

    private readonly AsyncLock _disposeLock = new();

    public async ValueTask DisposeAsync()
    {
        using var lockResult = await _disposeLock.LockAsync().Vhc();
        if (_disposed) return;
        _disposed = true;

        using var scope = VhLogger.Instance.BeginScope("Server");
        VhLogger.Instance.LogInformation("Server is shutting down...");

        // dispose update job
        _configureAndSendStatusJob.Dispose();

        // wait for configuration
        try {
            await _configureTask.Vhc();
        }
        catch {
            /* no error */
        }

        try {
            await _sendStatusTask.Vhc();
        }
        catch {
            /* no error*/
        }

        await ServerHost.DisposeAsync().Vhc(); // before disposing session manager to prevent recovering sessions
        await SessionManager.DisposeAsync().Vhc();
        _http01ChallengeService?.Dispose();
        if (_netConfigurationService != null)
            await _netConfigurationService.DisposeAsync().Vhc();

        if (_autoDisposeAccessManager)
            AccessManager.Dispose();

        State = ServerState.Disposed;
        VhLogger.Instance.LogInformation("Bye Bye!");
    }
}