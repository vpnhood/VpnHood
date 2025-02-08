using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Net;
using VpnHood.Core.Common.Trackers;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Server.Abstractions;
using VpnHood.Core.Server.Access;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Managers;
using VpnHood.Core.Server.SystemInformation;
using VpnHood.Core.Server.Utils;
using VpnHood.Core.Tunneling;

namespace VpnHood.Core.Server;

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
    private readonly NetConfigurationService? _netConfigurationService;
    private readonly ISystemInfoProvider _systemInfoProvider;
    private readonly ISwapMemoryProvider? _swapMemoryProvider;
    private string? _tcpCongestionControl;
    private bool _isRestarted = true;

    public ServerHost ServerHost { get; }
    public JobSection JobSection { get; }
    public static Version ServerVersion => typeof(VpnHoodServer).Assembly.GetName().Version;
    public SessionManager SessionManager { get; }
    public ServerState State { get; private set; } = ServerState.NotStarted;
    public IAccessManager AccessManager { get; }

    public VpnHoodServer(IAccessManager accessManager, ServerOptions options)
    {
        if (options.SocketFactory == null)
            throw new ArgumentNullException(nameof(options.SocketFactory));

        AccessManager = accessManager;
        _systemInfoProvider = options.SystemInfoProvider ?? new BasicSystemInfoProvider();
        JobSection = new JobSection(options.ConfigureInterval);
        SessionManager = new SessionManager(accessManager,
            options.NetFilter,
            options.SocketFactory,
            options.Tracker,
            tunProvider: options.TunProvider,
            serverVersion: ServerVersion,
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
        JobRunner.Default.Add(this);
    }

    public async Task RunJob()
    {
        if (_disposed) throw new ObjectDisposedException(VhLogger.FormatType(this));

        if (State == ServerState.Waiting && _configureTask.IsCompleted) {
            _configureTask = Configure(); // configure does not throw any error
            await _configureTask.VhConfigureAwait();
            return;
        }

        if (State == ServerState.Ready && _sendStatusTask.IsCompleted) {
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
        VhLogger.Instance.LogInformation("OS: {OS}", _systemInfoProvider.GetSystemInfo());
        VhLogger.Instance.LogInformation("IsDiagnoseMode: {IsDiagnoseMode}", VhLogger.IsDiagnoseMode);

        // Report TcpBuffers
        var tcpClient = new TcpClient();
        VhLogger.Instance.LogInformation(
            "DefaultTcpKernelSentBufferSize: {DefaultTcpKernelSentBufferSize}, DefaultTcpKernelReceiveBufferSize: {DefaultTcpKernelReceiveBufferSize}",
            tcpClient.SendBufferSize, tcpClient.ReceiveBufferSize);

        // Report Anonymous info
        _ = GaTrackStart();

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

        await RunJob().VhConfigureAwait();
    }

    private async Task Configure()
    {
        try {
            VhLogger.Instance.LogInformation("Configuring by the Access Manager...");
            State = ServerState.Configuring;

            // get server info
            VhLogger.Instance.LogTrace("Finding free EndPoints...");
            var freeUdpPortV4 = ServerUtil.GetFreeUdpPort(AddressFamily.InterNetwork, null);
            var freeUdpPortV6 = ServerUtil.GetFreeUdpPort(AddressFamily.InterNetworkV6, freeUdpPortV4);

            VhLogger.Instance.LogTrace("Finding public addresses...");
            var privateIpAddresses = await IPAddressUtil.GetPrivateIpAddresses().VhConfigureAwait();

            VhLogger.Instance.LogTrace("Finding public addresses..., PublicIpDiscovery: {PublicIpDiscovery}",
                _publicIpDiscovery);
            var publicIpAddresses = _publicIpDiscovery
                ? await IPAddressUtil.GetPublicIpAddresses(CancellationToken.None).VhConfigureAwait()
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
            VhLogger.Instance.LogTrace("Sending config request to the Access Server...");
            var serverConfig = await ReadConfig(serverInfo).VhConfigureAwait();
            VhLogger.IsAnonymousMode = serverConfig.LogAnonymizerValue;
            SessionManager.TrackingOptions = serverConfig.TrackingOptions;
            SessionManager.SessionOptions = serverConfig.SessionOptions;
            SessionManager.ServerSecret = serverConfig.ServerSecret ?? SessionManager.ServerSecret;
            JobSection.Interval = serverConfig.UpdateStatusIntervalValue < serverConfig.SessionOptions.SyncIntervalValue
                ? serverConfig.UpdateStatusIntervalValue // update status interval must be less than sync interval
                : serverConfig.SessionOptions.SyncIntervalValue;

            ServerUtil.ConfigMinIoThreads(serverConfig.MinCompletionPortThreads);
            ServerUtil.ConfigMaxIoThreads(serverConfig.MaxCompletionPortThreads);
            var allServerIps = serverInfo.PublicIpAddresses
                .Concat(serverInfo.PrivateIpAddresses)
                .Concat(serverConfig.TcpEndPoints?.Select(x => x.Address) ?? Array.Empty<IPAddress>());

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
                        .AddIpAddress(ipEndPoint.Address, serverConfig.AddListenerIpsToNetwork).VhConfigureAwait();
            }

            // Set TcpCongestionControl
            if (_netConfigurationService != null && !string.IsNullOrEmpty(serverConfig.TcpCongestionControlValue)) {
                await _netConfigurationService.SetTcpCongestionControl(serverConfig.TcpCongestionControlValue)
                    .VhConfigureAwait();
            }

            _tcpCongestionControl = _netConfigurationService != null
                ? await _netConfigurationService.GetTcpCongestionControl()
                : null;

            if (_swapMemoryProvider != null) {
                await ConfigureSwapMemory(serverConfig.SwapMemorySizeMb);
            }

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
        catch (Exception ex) {
            State = ServerState.Waiting;
            _lastConfigError = new ApiError(ex);
            if (ex is SocketException socketException)
                _lastConfigError.Data.Add("SocketErrorCode", socketException.SocketErrorCode.ToString());

            SessionManager.Tracker?.VhTrackErrorAsync(ex, "Could not configure server!", "Configure");
            VhLogger.Instance.LogError(ex, "Could not configure server! Retrying after {TotalSeconds} seconds.",
                JobSection.Interval.TotalSeconds);
            await SendStatusToAccessManager(false).VhConfigureAwait();
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
            var size = sizeMb * VhUtil.Megabytes ?? 0;
            var otherSize = info.TotalSize - info.AppSize;
            var newAppSize = Math.Max(0, size - otherSize);

            if (Math.Abs(info.AppSize - newAppSize) < VhUtil.Megabytes) {
                if (size == 0 && info.AppSize == 0)
                    return; // there is no need to configure swap file

                VhLogger.Instance.LogInformation(
                    "Swap file size is already near to the requested size. CurrentSize: {CurrentSize}, RequestedSize: {RequestedSize}",
                    VhUtil.FormatBytes(info.TotalSize), VhUtil.FormatBytes(size));
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
            netFilter.BlockedIpRanges = netFilter.BlockedIpRanges.Exclude(virtualIpNetworkV4.ToIpRange());
            netFilter.BlockedIpRanges = netFilter.BlockedIpRanges.Exclude(virtualIpNetworkV6.ToIpRange());
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
        var serverConfig = await ReadConfigImpl(serverInfo).VhConfigureAwait();
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
            ? VhUtil.RedactJsonValue(json, [
                nameof(ServerInfo.PrivateIpAddresses),
                nameof(ServerInfo.PublicIpAddresses)
            ])
            : json;
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
            : VhUtil.RedactJsonValue(json, [
                nameof(CertificateData.RawData) // it will ruin the report and useless to see
            ]);
    }

    private async Task<ServerConfig> ReadConfigImpl(ServerInfo serverInfo)
    {
        try {
            var serverConfig = await AccessManager.Server_Configure(serverInfo).VhConfigureAwait();
            _isRestarted = false; // is restarted should send once
            try {
                await File.WriteAllTextAsync(_lastConfigFilePath, JsonSerializer.Serialize(serverConfig))
                    .VhConfigureAwait();
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
                    var configJson = await File.ReadAllTextAsync(_lastConfigFilePath).VhConfigureAwait();
                    var ret = VhUtil.JsonDeserialize<ServerConfig>(configJson);
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
            ConfigError = _lastConfigError?.ToJson()
        };
        return serverStatus;
    }

    private async Task SendStatusToAccessManager(bool allowConfigure)
    {
        try {
            var status = await GetStatus();
            VhLogger.Instance.LogTrace("Sending status to Access... ConfigCode: {ConfigCode}", status.ConfigCode);
            status.SessionUsages = SessionManager.CollectSessionUsages();
            var res = await AccessManager.Server_UpdateStatus(status).VhConfigureAwait();
            SessionManager.ApplySessionResponses(res.SessionResponses);

            // reconfigure
            if (allowConfigure && res.ConfigCode != _lastConfigCode) {
                VhLogger.Instance.LogInformation("Reconfiguration was requested.");
                await Configure().VhConfigureAwait();
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not send the server status.");
        }
    }

    private Task GaTrackStart()
    {
        if (SessionManager.Tracker == null)
            return Task.CompletedTask;

        // track
        return SessionManager.Tracker.Track(new TrackEvent {
            EventName = TrackEventNames.SessionStart,
            Parameters = new Dictionary<string, object> {
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
        try {
            await _configureTask.VhConfigureAwait();
        }
        catch {
            /* no error */
        }

        try {
            await _sendStatusTask.VhConfigureAwait();
        }
        catch {
            /* no error*/
        }

        await ServerHost.DisposeAsync()
            .VhConfigureAwait(); // before disposing session manager to prevent recovering sessions
        await SessionManager.DisposeAsync().VhConfigureAwait();
        _http01ChallengeService?.Dispose();
        if (_netConfigurationService != null)
            await _netConfigurationService.DisposeAsync().VhConfigureAwait();

        if (_autoDisposeAccessManager)
            AccessManager.Dispose();

        State = ServerState.Disposed;
        VhLogger.Instance.LogInformation("Bye Bye!");
    }
}