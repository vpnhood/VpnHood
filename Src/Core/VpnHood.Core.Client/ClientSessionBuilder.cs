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

internal class ClientSessionBuilder(
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
    public async Task<ClientSession> Build(
        CancellationToken disposeCancellationToken,
        CancellationToken cancellationToken = default)
    {
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(disposeCancellationToken, cancellationToken);

        setState(ClientState.Connecting);

        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
        VhLogger.Instance.LogInformation(
            "DropUdp: {DropUdp}, VpnProtocol: {VpnProtocol}, " +
            "IncludeLocalNetwork: {IncludeLocalNetwork}, MinWorkerThreads: {WorkerThreads}, " +
            "CompletionPortThreads: {CompletionPortThreads}, ClientIpV6: {ClientIpV6}, ProcessId: {ProcessId}",
            config.DropUdp, channelProtocol, config.IncludeLocalNetwork, workerThreads, completionPortThreads,
            vpnAdapter.IsIpVersionSupported(IpVersion.IPv6), Process.GetCurrentProcess().Id);

        VhLogger.Instance.LogInformation(
            "ClientVersion: {ClientVersion}, " +
            "ClientMinProtocolVersion: {ClientMinProtocolVersion}, ClientMaxProtocolVersion: {ClientMaxProtocolVersion}, " +
            "ClientId: {ClientId}",
            config.Version, VpnHoodClientConfig.MinProtocolVersion, VpnHoodClientConfig.MaxProtocolVersion,
            VhLogger.FormatId(config.ClientId));

        if (proxyEndPointManager.IsEnabled) {
            setState(ClientState.ValidatingProxies);
            await proxyEndPointManager.CheckServers(linkedCts.Token).Vhc();

            VhLogger.Instance.LogInformation("Proxy servers: {Count}",
                proxyEndPointManager.Status.ProxyEndPointInfos.Count(x => x.Status.ErrorMessage is null));

            if (!proxyEndPointManager.Status.IsAnySucceeded)
                throw new UnreachableProxyServerException();
        }

        setState(ClientState.FindingReachableServer);
        var vpnEndPoint = await serverFinder.FindReachableServerAsync([token.ServerToken], linkedCts.Token).Vhc();
        var allowRedirect = !serverFinder.CustomServerEndpoints.Any();
        return await Connect(vpnEndPoint, allowRedirect, linkedCts.Token).Vhc();
    }

    private async Task<ClientSession> Connect(
        VpnEndPoint vpnEndPoint,
        bool allowRedirect,
        CancellationToken cancellationToken)
    {
        ConnectorService? connectorService = null;

        try {
            VhLogger.Instance.LogInformation("Connecting to the server... EndPoint: {hostEndPoint}",
                VhLogger.Format(vpnEndPoint.TcpEndPoint));
            setState(ClientState.Connecting);

            connectorService = new ConnectorService(
                options: new ConnectorServiceOptions(
                    ProxyEndPointManager: proxyEndPointManager,
                    SocketFactory: socketFactory,
                    VpnEndPoint: vpnEndPoint,
                    RequestTimeout: config.TcpConnectTimeout,
                    AllowTcpReuse: false));

            var clientInfo = new ClientInfo {
                ClientId = config.ClientId,
                ClientVersion = config.Version.ToString(3),
                MinProtocolVersion = connectorService.ProtocolVersion,
                MaxProtocolVersion = VpnHoodClientConfig.MaxProtocolVersion,
                UserAgent = config.UserAgent
            };

            var request = new HelloRequest {
                RequestId = UniqueIdFactory.Create(),
                EncryptedClientId = VhUtils.EncryptClientId(clientInfo.ClientId, token.Secret),
                ClientInfo = clientInfo,
                TokenId = token.TokenId,
                ServerLocation = serverFinder.ServerLocation,
                PlanId = config.PlanId,
                AccessCode = config.AccessCode,
                AllowRedirect = allowRedirect,
                IsIpV6Supported = vpnAdapter.IsIpVersionSupported(IpVersion.IPv6),
                UserReview = config.UserReview
            };

            using var requestResult = await connectorService.SendRequest<HelloResponse>(request, cancellationToken).Vhc();
            requestResult.Connection.PreventReuse();
            connectorService.AllowTcpReuse = config.AllowTcpReuse;

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
                useWebSocket: config.UseWebSocket);

            var sessionId = helloResponse.SessionId;
            var sessionKey = helloResponse.SessionKey;

            var hostUdpEndPoint = helloResponse is { UdpPort: > 0, ProtocolVersion: >= 11 }
                ? new IPEndPoint(connectorService.VpnEndPoint.TcpEndPoint.Address, helloResponse.UdpPort.Value)
                : null;

            staticIpFilter.IncludeRanges = config.IncludeIpRangesByApp.ToOrderedList();
            var dnsConfig = ClientHelper.GetDnsServers(
                config.DnsServers,
                serverDnsAddresses: helloResponse.DnsServers ?? [],
                serverIncludeIpRanges: serverIncludeIpRangesByApp,
                ipFilter: staticIpFilter);

            var sessionIncludeIpRangesByDevice = ClientHelper.BuildIncludeIpRangesByDevice(
                includeIpRanges: serverIncludeIpRangesByDevice.Intersect(config.IncludeIpRangesByDevice),
                canProtectSocket: vpnAdapter.CanProtectSocket,
                includeLocalNetwork: config.IncludeLocalNetwork,
                catcherIps: [config.TcpProxyCatcherAddressIpV4, config.TcpProxyCatcherAddressIpV6],
                hostIpAddress: connectorService.VpnEndPoint.TcpEndPoint.Address);

            staticIpFilter.IncludeRanges = staticIpFilter.IncludeRanges
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

            if (!helloResponse.IsTcpPacketSupported && !config.IsTcpProxySupported)
                throw new NotSupportedException(
                    "The server does not support any protocol to support your client.");

            if (!helloResponse.IsTcpPacketSupported && !config.UseTcpProxy)
                VhLogger.Instance.LogWarning("TCP Proxy enabled because the server does not support TCP packets.");

            if (!helloResponse.IsTcpProxySupported && config.UseTcpProxy)
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

            if (config.AllowAnonymousTracker) {
                if (!string.IsNullOrEmpty(helloResponse.GaMeasurementId)) {
                    var ga4Tracking = new Ga4TagTracker {
                        SessionCount = 1,
                        MeasurementId = helloResponse.GaMeasurementId,
                        ClientId = config.ClientId,
                        SessionId = helloResponse.SessionId.ToString(),
                        UserAgent = config.UserAgent,
                        UserProperties = new Dictionary<string, object>
                            { { "client_version", config.Version.ToString(3) } }
                    };

                    _ = ga4Tracking.TryTrack(new Ga4TagEvent { EventName = TrackEventNames.SessionStart },
                        VhLogger.Instance);
                }

                if (tracker != null) {
                    _ = tracker.TryTrack(
                        ClientTrackerBuilder.BuildConnectionSucceeded(
                            serverFinder.ServerLocation,
                            isIpV6Supported: vpnAdapter.IsIpVersionSupported(IpVersion.IPv6),
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
                SessionName = config.SessionName,
                ExcludeApps = config.ExcludeApps,
                IncludeApps = config.IncludeApps
            };

            var session = new ClientSession(
                options: new ClientSessionOptions {
                    VpnAdapter = vpnAdapter,
                    SocketFactory = socketFactory,
                    Tracker = tracker,
                    AccessUsage = helloResponse.AccessUsage ?? new AccessUsage(),
                    ConnectorService = connectorService,
                    DomainFilteringService = domainFilteringService,
                    NetFilter = netFilter,
                    ChannelProtocol = channelProtocol,
                    DropQuic = config.DropQuic,
                    DropUdp = config.DropUdp,
                    UseTcpProxy = config.UseTcpProxy
                },
                config: new ClientSessionConfig {
                    SessionInfo = sessionInfo,
                    AdapterOptions = adapterOptions,
                    SessionId = sessionId,
                    SessionKey = sessionKey,
                    TcpProxyCatcherAddressIpV4 = config.TcpProxyCatcherAddressIpV4,
                    TcpProxyCatcherAddressIpV6 = config.TcpProxyCatcherAddressIpV6,
                    RemoteMtu = helloResponse.Mtu,
                    MaxPacketChannelLifespan = config.MaxPacketChannelLifespan,
                    MinPacketChannelLifespan = config.MinPacketChannelLifespan,
                    SessionTimeout = config.SessionTimeout,
                    TcpConnectTimeout = config.TcpConnectTimeout,
                    StreamProxyBufferSize = config.StreamProxyBufferSize,
                    UdpProxyBufferSize = config.UdpProxyBufferSize,
                    UnstableTimeout = config.UnstableTimeout,
                    AutoWaitTimeout = config.AutoWaitTimeout,
                    DnsConfig = dnsConfig,
                    IsTcpProxySupported = config.IsTcpProxySupported,
                    HostTcpEndPoint = connectorService.VpnEndPoint.TcpEndPoint,
                    HostUdpEndPoint = hostUdpEndPoint,
                    IsIpV6SupportedByServer = helloResponse.IsIpV6Supported,
                    AdRequirement = helloResponse.AdRequirement,
                    MaxPacketChannelCount = helloResponse.MaxPacketChannelCount != 0
                        ? Math.Min(config.MaxPacketChannelCount, helloResponse.MaxPacketChannelCount)
                        : config.MaxPacketChannelCount,
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

            setState(ClientState.FindingBestServer);
            var redirectServerTokens = ex.RedirectServerTokens;

#pragma warning disable CS0618
            if (redirectServerTokens is null) {
                var serverToken = JsonUtils.JsonClone(token.ServerToken);
                serverToken.HostEndPoints = ex.RedirectHostEndPoints;
                redirectServerTokens = [serverToken];
            }
#pragma warning restore CS0618

            var redirectedEndPoint = await serverFinder
                .FindBestRedirectedServerAsync(redirectServerTokens, cancellationToken).Vhc();
            return await Connect(redirectedEndPoint, false, cancellationToken).Vhc();
        }
        catch {
            connectorService?.Dispose();
            throw;
        }
    }
}