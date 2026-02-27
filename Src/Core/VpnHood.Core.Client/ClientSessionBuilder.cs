using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Trackers;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Proxies.EndPointManagement;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client;

internal class ClientSessionBuilder
{
    private readonly IVpnAdapter _vpnAdapter;
    private readonly ISocketFactory _socketFactory;
    private readonly Token _token;
    private readonly VpnHoodClientConfig _config;
    private readonly ITracker? _tracker;
    private readonly ServerFinder _serverFinder;
    private readonly ProxyEndPointManager _proxyEndPointManager;
    private readonly DomainFilteringService _domainFilteringService;
    private readonly NetFilter _netFilter;
    private readonly StaticIpFilter _staticIpFilter;
    private readonly ChannelProtocol _channelProtocol;
    private readonly Action<ClientState> _setState;

    public ClientSessionBuilder(
        IVpnAdapter vpnAdapter,
        ISocketFactory socketFactory,
        Token token,
        VpnHoodClientConfig config,
        ITracker? tracker,
        ServerFinder serverFinder,
        ProxyEndPointManager proxyEndPointManager,
        DomainFilteringService domainFilteringService,
        NetFilter netFilter,
        StaticIpFilter staticIpFilter,
        ChannelProtocol channelProtocol,
        Action<ClientState> setState)
    {
        _vpnAdapter = vpnAdapter;
        _socketFactory = socketFactory;
        _token = token;
        _config = config;
        _tracker = tracker;
        _serverFinder = serverFinder;
        _proxyEndPointManager = proxyEndPointManager;
        _domainFilteringService = domainFilteringService;
        _netFilter = netFilter;
        _staticIpFilter = staticIpFilter;
        _channelProtocol = channelProtocol;
        _setState = setState;
    }

    public async Task<ClientSession> Build(
        CancellationToken disposeCancellationToken,
        CancellationToken cancellationToken = default)
    {
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(disposeCancellationToken, cancellationToken);

        _setState(ClientState.Connecting);

        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
        VhLogger.Instance.LogInformation(
            "DropUdp: {DropUdp}, VpnProtocol: {VpnProtocol}, " +
            "IncludeLocalNetwork: {IncludeLocalNetwork}, MinWorkerThreads: {WorkerThreads}, " +
            "CompletionPortThreads: {CompletionPortThreads}, ClientIpV6: {ClientIpV6}, ProcessId: {ProcessId}",
            _config.DropUdp, _channelProtocol, _config.IncludeLocalNetwork, workerThreads, completionPortThreads,
            _vpnAdapter.IsIpVersionSupported(IpVersion.IPv6), Process.GetCurrentProcess().Id);

        VhLogger.Instance.LogInformation(
            "ClientVersion: {ClientVersion}, " +
            "ClientMinProtocolVersion: {ClientMinProtocolVersion}, ClientMaxProtocolVersion: {ClientMaxProtocolVersion}, " +
            "ClientId: {ClientId}",
            _config.Version, VpnHoodClientConfig.MinProtocolVersion, VpnHoodClientConfig.MaxProtocolVersion,
            VhLogger.FormatId(_config.ClientId));

        if (_proxyEndPointManager.IsEnabled) {
            _setState(ClientState.ValidatingProxies);
            await _proxyEndPointManager.CheckServers(linkedCts.Token).Vhc();

            VhLogger.Instance.LogInformation("Proxy servers: {Count}",
                _proxyEndPointManager.Status.ProxyEndPointInfos.Count(x => x.Status.ErrorMessage is null));

            if (!_proxyEndPointManager.Status.IsAnySucceeded)
                throw new UnreachableProxyServerException();
        }

        _setState(ClientState.FindingReachableServer);
        var vpnEndPoint = await _serverFinder.FindReachableServerAsync([_token.ServerToken], linkedCts.Token).Vhc();
        var allowRedirect = !_serverFinder.CustomServerEndpoints.Any();
        return await ConnectInternal(vpnEndPoint, allowRedirect, linkedCts.Token).Vhc();
    }

    private async Task<ClientSession> ConnectInternal(
        VpnEndPoint vpnEndPoint,
        bool allowRedirect,
        CancellationToken cancellationToken)
    {
        ConnectorService? connectorService = null;

        try {
            VhLogger.Instance.LogInformation("Connecting to the server... EndPoint: {hostEndPoint}",
                VhLogger.Format(vpnEndPoint.TcpEndPoint));
            _setState(ClientState.Connecting);

            connectorService = new ConnectorService(
                options: new ConnectorServiceOptions(
                    ProxyEndPointManager: _proxyEndPointManager,
                    SocketFactory: _socketFactory,
                    VpnEndPoint: vpnEndPoint,
                    RequestTimeout: _config.TcpConnectTimeout,
                    AllowTcpReuse: false));

            var clientInfo = new ClientInfo {
                ClientId = _config.ClientId,
                ClientVersion = _config.Version.ToString(3),
                MinProtocolVersion = connectorService.ProtocolVersion,
                MaxProtocolVersion = VpnHoodClientConfig.MaxProtocolVersion,
                UserAgent = _config.UserAgent
            };

            var request = new HelloRequest {
                RequestId = UniqueIdFactory.Create(),
                EncryptedClientId = VhUtils.EncryptClientId(clientInfo.ClientId, _token.Secret),
                ClientInfo = clientInfo,
                TokenId = _token.TokenId,
                ServerLocation = _serverFinder.ServerLocation,
                PlanId = _config.PlanId,
                AccessCode = _config.AccessCode,
                AllowRedirect = allowRedirect,
                IsIpV6Supported = _vpnAdapter.IsIpVersionSupported(IpVersion.IPv6),
                UserReview = _config.UserReview
            };

            using var requestResult = await connectorService.SendRequest<HelloResponse>(request, cancellationToken).Vhc();
            requestResult.Connection.PreventReuse();
            connectorService.AllowTcpReuse = _config.AllowTcpReuse;

            var helloResponse = requestResult.Response;
            if (helloResponse.ClientPublicAddress is null)
                throw new NotSupportedException($"Server must returns {nameof(helloResponse.ClientPublicAddress)}.");

            var serverIncludeIpRangesByApp = helloResponse.IncludeIpRanges?.ToOrderedList() ?? IpNetwork.All.ToIpRanges();
            var serverIncludeIpRangesByDevice = helloResponse.VpnAdapterIncludeIpRanges?.ToOrderedList() ?? IpNetwork.All.ToIpRanges();
            var serverIncludeIpRanges = serverIncludeIpRangesByApp.Intersect(serverIncludeIpRangesByDevice);
            var serverAllowedLocalNetworks = IpNetwork.LocalNetworks.ToIpRanges().Intersect(serverIncludeIpRanges);

            VhLogger.Instance.LogInformation(GeneralEventId.Session,
                "Hurray! Client has been connected! " +
                $"SessionId: {VhLogger.FormatId(helloResponse.SessionId)}, " +
                $"ServerVersion: {helloResponse.ServerVersion}, " +
                $"ProtocolVersion: {helloResponse.ProtocolVersion}, " +
                $"CurrentProtocolVersion: {connectorService.ProtocolVersion}, " +
                $"ClientIp: {VhLogger.Format(helloResponse.ClientPublicAddress)}, " +
                $"UdpPort: {helloResponse.UdpPort}, " +
                $"IsTcpPacketSupported: {helloResponse.IsTcpPacketSupported}, " +
                $"IsTcpProxySupported: {helloResponse.IsTcpProxySupported}, " +
                $"IsLocalNetworkAllowed: {serverAllowedLocalNetworks.Any()}, " +
                $"NetworkV4: {helloResponse.VirtualIpNetworkV4}, " +
                $"NetworkV6: {helloResponse.VirtualIpNetworkV6}, " +
                $"ClientCountry: {helloResponse.ClientCountry}");

            connectorService.Init(
                helloResponse.ProtocolVersion,
                requestTimeout: helloResponse.RequestTimeout.WhenNoDebugger(),
                tcpReuseTimeout: helloResponse.TcpReuseTimeout,
                serverSecret: helloResponse.ServerSecret,
                useWebSocket: _config.UseWebSocket);

            var sessionId = helloResponse.SessionId;
            var sessionKey = helloResponse.SessionKey;

            var hostUdpEndPoint = helloResponse is { UdpPort: > 0, ProtocolVersion: >= 11 }
                ? new IPEndPoint(connectorService.VpnEndPoint.TcpEndPoint.Address, helloResponse.UdpPort.Value)
                : null;

            _staticIpFilter.IncludeRanges = _config.IncludeIpRangesByApp.ToOrderedList();
            var dnsConfig = ClientHelper.GetDnsServers(
                _config.DnsServers,
                serverDnsAddresses: helloResponse.DnsServers ?? [],
                serverIncludeIpRanges: serverIncludeIpRangesByApp,
                ipFilter: _staticIpFilter);

            var sessionIncludeIpRangesByDevice = ClientHelper.BuildIncludeIpRangesByDevice(
                includeIpRanges: serverIncludeIpRangesByDevice.Intersect(_config.IncludeIpRangesByDevice),
                canProtectSocket: _vpnAdapter.CanProtectSocket,
                includeLocalNetwork: _config.IncludeLocalNetwork,
                catcherIps: [_config.TcpProxyCatcherAddressIpV4, _config.TcpProxyCatcherAddressIpV6],
                hostIpAddress: connectorService.VpnEndPoint.TcpEndPoint.Address);

            _staticIpFilter.IncludeRanges = _staticIpFilter.IncludeRanges
                .Intersect(serverIncludeIpRanges)
                .Intersect(sessionIncludeIpRangesByDevice);

            if (helloResponse.SuppressedTo == SessionSuppressType.YourSelf)
                VhLogger.Instance.LogWarning("You suppressed a session of yourself!");
            else if (helloResponse.SuppressedTo == SessionSuppressType.Other)
                VhLogger.Instance.LogWarning("You suppressed a session of another client!");

            if (hostUdpEndPoint is null)
                VhLogger.Instance.LogWarning("The server does not support UDP channel.");

            if (helloResponse is { IsTcpPacketSupported: false, IsTcpProxySupported: false })
                throw new NotSupportedException(
                    "The server does not support any protocol to support TCP. Please contact support.");

            if (!helloResponse.IsTcpPacketSupported && !_config.IsTcpProxySupported)
                throw new NotSupportedException(
                    "The server does not support any protocol to support your client.");

            if (!helloResponse.IsTcpPacketSupported && !_config.UseTcpProxy)
                VhLogger.Instance.LogWarning("TCP Proxy enabled because the server does not support TCP packets.");

            if (!helloResponse.IsTcpProxySupported && _config.UseTcpProxy)
                VhLogger.Instance.LogWarning("TCP Proxy disabled because the server does not support it.");

            var sessionInfo = new SessionInfo {
                SessionId = helloResponse.SessionId.ToString(),
                ClientPublicIpAddress = helloResponse.ClientPublicAddress,
                ClientCountry = helloResponse.ClientCountry,
                AccessInfo = helloResponse.AccessInfo ?? new AccessInfo(),
                IsLocalNetworkAllowed = serverAllowedLocalNetworks.Any(),
                DnsConfig = dnsConfig,
                IsPremiumSession = helloResponse.AccessUsage?.IsPremium ?? false,
                IsUdpChannelSupported = hostUdpEndPoint != null,
                AccessKey = helloResponse.AccessKey,
                ServerVersion = Version.Parse(helloResponse.ServerVersion),
                SuppressedTo = helloResponse.SuppressedTo,
                AdRequirement = helloResponse.AdRequirement,
                CreatedTime = DateTime.UtcNow,
                IsTcpPacketSupported = helloResponse.IsTcpPacketSupported,
                IsTcpProxySupported = helloResponse.IsTcpProxySupported,
                ChannelProtocols = ChannelProtocolValidator.GetChannelProtocols(helloResponse),
                ServerLocationInfo = helloResponse.ServerLocation != null
                    ? ServerLocationInfo.Parse(helloResponse.ServerLocation)
                    : null
            };

            if (_config.AllowAnonymousTracker) {
                if (!string.IsNullOrEmpty(helloResponse.GaMeasurementId)) {
                    var ga4Tracking = new Ga4TagTracker {
                        SessionCount = 1,
                        MeasurementId = helloResponse.GaMeasurementId,
                        ClientId = _config.ClientId,
                        SessionId = helloResponse.SessionId.ToString(),
                        UserAgent = _config.UserAgent,
                        UserProperties = new Dictionary<string, object>
                            { { "client_version", _config.Version.ToString(3) } }
                    };

                    _ = ga4Tracking.TryTrack(new Ga4TagEvent { EventName = TrackEventNames.SessionStart },
                        VhLogger.Instance);
                }

                if (_tracker != null) {
                    _ = _tracker.TryTrack(
                        ClientTrackerBuilder.BuildConnectionSucceeded(
                            _serverFinder.ServerLocation,
                            isIpV6Supported: _vpnAdapter.IsIpVersionSupported(IpVersion.IPv6),
                            hasRedirected: !allowRedirect,
                            endPoint: connectorService.VpnEndPoint.TcpEndPoint,
                            adNetworkName: null));
                }
            }

            var networkV4 = helloResponse.VirtualIpNetworkV4 ?? new IpNetwork(IPAddress.Parse("10.255.0.2"), 32);
            var networkV6 = helloResponse.VirtualIpNetworkV6 ??
                            new IpNetwork(IPAddressUtil.GenerateUlaAddress(0x1001), 128);
            VhLogger.Instance.LogInformation(
                "Starting VpnAdapter... DnsServers: {DnsServers}, IncludeNetworks: {longIncludeNetworks}",
                sessionInfo.DnsConfig, VhLogger.Format(sessionIncludeIpRangesByDevice.ToIpNetworks()));

            var adapterOptions = new VpnAdapterOptions {
                DnsServers = dnsConfig.DnsServers,
                VirtualIpNetworkV4 = networkV4,
                VirtualIpNetworkV6 = networkV6,
                Mtu = helloResponse.Mtu - TunnelDefaults.MtuOverhead,
                IncludeNetworks = sessionIncludeIpRangesByDevice.ToIpNetworks(),
                SessionName = _config.SessionName,
                ExcludeApps = _config.ExcludeApps,
                IncludeApps = _config.IncludeApps
            };

            var session = new ClientSession(
                options: new ClientSessionOptions {
                    VpnAdapter = _vpnAdapter,
                    SocketFactory = _socketFactory,
                    Tracker = _tracker,
                    SessionInfo = sessionInfo,
                    AccessUsage = helloResponse.AccessUsage ?? new AccessUsage(),
                    ConnectorService = connectorService,
                    DomainFilteringService = _domainFilteringService,
                    NetFilter = _netFilter,
                    VpnAdapterOptions = adapterOptions,
                    ChannelProtocol = _channelProtocol,
                    DropQuic = _config.DropQuic,
                    DropUdp = _config.DropUdp,
                    UseTcpProxy = _config.UseTcpProxy
                },
                config: new ClientSessionConfig {
                    SessionId = sessionId,
                    SessionKey = sessionKey,
                    TcpProxyCatcherAddressIpV4 = _config.TcpProxyCatcherAddressIpV4,
                    TcpProxyCatcherAddressIpV6 = _config.TcpProxyCatcherAddressIpV6,
                    RemoteMtu = helloResponse.Mtu,
                    MaxPacketChannelLifespan = _config.MaxPacketChannelLifespan,
                    MinPacketChannelLifespan = _config.MinPacketChannelLifespan,
                    SessionTimeout = _config.SessionTimeout,
                    TcpConnectTimeout = _config.TcpConnectTimeout,
                    StreamProxyBufferSize = _config.StreamProxyBufferSize,
                    UdpProxyBufferSize = _config.UdpProxyBufferSize,
                    UnstableTimeout = _config.UnstableTimeout,
                    AutoWaitTimeout = _config.AutoWaitTimeout,
                    DnsConfig = dnsConfig,
                    IsTcpProxySupported = _config.IsTcpProxySupported,
                    HostTcpEndPoint = connectorService.VpnEndPoint.TcpEndPoint,
                    HostUdpEndPoint = hostUdpEndPoint,
                    IsIpV6SupportedByServer = helloResponse.IsIpV6Supported,
                    AdRequirement = helloResponse.AdRequirement,
                    MaxPacketChannelCount = helloResponse.MaxPacketChannelCount != 0
                        ? Math.Min(_config.MaxPacketChannelCount, helloResponse.MaxPacketChannelCount)
                        : _config.MaxPacketChannelCount,
                });

            return session;
        }
        catch (TimeoutException) {
            connectorService?.Dispose();
            throw new ConnectionTimeoutException("Could not connect to the server in the given time.");
        }
        catch (RedirectHostException ex) {
            connectorService?.Dispose();
            if (!allowRedirect) {
                VhLogger.Instance.LogError(ex,
                    "The server replies with a redirect to another server again. We already redirected earlier. This is unexpected.");
                throw;
            }

            _setState(ClientState.FindingBestServer);
            var redirectServerTokens = ex.RedirectServerTokens;

#pragma warning disable CS0618
            if (redirectServerTokens is null) {
                var serverToken = JsonUtils.JsonClone(_token.ServerToken);
                serverToken.HostEndPoints = ex.RedirectHostEndPoints;
                redirectServerTokens = [serverToken];
            }
#pragma warning restore CS0618

            var redirectedEndPoint = await _serverFinder
                .FindBestRedirectedServerAsync(redirectServerTokens, cancellationToken).Vhc();
            return await ConnectInternal(redirectedEndPoint, false, cancellationToken).Vhc();
        }
        catch {
            connectorService?.Dispose();
            throw;
        }
    }
}