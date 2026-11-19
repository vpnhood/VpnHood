using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Common.Trackers;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.DomainFiltering;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Client;

internal class ClientSessionBuilder(
    IVpnAdapter vpnAdapter,
    IClientSocketFactory socketFactory,
    Token token,
    VpnHoodClientConfig config,
    ITracker? tracker,
    ServerFinder serverFinder,
    ConnectedServerFile connectedServerFile,
    IProxyConnector? proxyConnector,
    DomainFilteringService domainFilteringService,
    NetFilter netFilter,
    ServerIpFilter serverIpFilter,
    ChannelProtocol channelProtocol,
    bool useTcpForwarding,
    bool dropUdp,
    bool dropQuic,
    Action<ClientState> setState)
{
    public async Task<ClientSession> Build(
        CancellationToken disposeCancellationToken,
        CancellationToken cancellationToken)
    {
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(disposeCancellationToken, cancellationToken);

        setState(ClientState.Connecting);

        ThreadPool.GetMinThreads(out var workerThreads, out var completionPortThreads);
        VhLogger.Instance.LogInformation(
            "DropUdp: {DropUdp}, VpnProtocol: {VpnProtocol}, " +
            "IncludeLocalNetwork: {IncludeLocalNetwork}, MinWorkerThreads: {WorkerThreads}, " +
            "CompletionPortThreads: {CompletionPortThreads}, ClientIpV6: {ClientIpV6}, ProcessId: {ProcessId}",
            dropUdp, channelProtocol, config.IncludeLocalNetwork, workerThreads, completionPortThreads,
            vpnAdapter.IsIpVersionSupported(IpVersion.IPv6), Process.GetCurrentProcess().Id);

        VhLogger.Instance.LogInformation(
            "ClientVersion: {ClientVersion}, " +
            "ClientMinProtocolVersion: {ClientMinProtocolVersion}, ClientMaxProtocolVersion: {ClientMaxProtocolVersion}, " +
            "ClientId: {ClientId}",
            config.Version, VpnHoodClientConfig.MinProtocolVersion, VpnHoodClientConfig.MaxProtocolVersion,
            VhLogger.FormatId(config.ClientId));

        var connectedServer = await connectedServerFile.Read(linkedCts.Token).Vhc();
        // Checked against a server they are for, the last one connected or one of the access key's, as the device
        // reaches no one else through them, with the VPN's own check of its certificate. Their failure ends nothing
        // here: that server may be down where another is not, and the connections to each server check the proxies
        // against it again. A key whose servers are known only by name is left to those checks, as is its lookup
        var checkEndPoint = connectedServer?.TcpEndPoint ??
                            (serverFinder.CustomServerEndpoints.Count > 0
                                ? serverFinder.CustomServerEndpoints
                                : token.ServerToken.HostEndPoints ?? []).Shuffle().FirstOrDefault();

        if (proxyConnector is { IsEnabled: true } && checkEndPoint != null) {
            setState(ClientState.ValidatingProxies);
            var certificateHash = connectedServer?.CertificateHash ?? token.ServerToken.CertificateHash;
            await proxyConnector.CheckServers(socketFactory, checkEndPoint,
                connectedServer?.HostName ?? token.ServerToken.HostName,
                (_, certificate, chain, errors) =>
                    ConnectorService.IsServerCertificateValid(certificateHash, certificate, chain, errors),
                linkedCts.Token).Vhc();

            VhLogger.Instance.LogInformation("Proxy servers succeeded: {Count}",
                proxyConnector.Status.SucceededServerCount);
        }

        setState(ClientState.FindingReachableServer);
        var vpnEndPoint = await serverFinder.FindReachableServerAsync([token.ServerToken], connectedServer,
            linkedCts.Token).Vhc();
        var allowRedirect = !serverFinder.CustomServerEndpoints.Any();
        return await Connect(vpnEndPoint, allowRedirect, linkedCts.Token).Vhc();
    }

    private async Task<ClientSession> Connect(
        VpnEndPoint vpnEndPoint,
        bool allowRedirect,
        CancellationToken cancellationToken)
    {
        RequestSender? requestSender = null;

        try {
            VhLogger.Instance.LogInformation("Connecting to the server... EndPoint: {hostEndPoint}",
                VhLogger.Format(vpnEndPoint.TcpEndPoint));
            setState(ClientState.Connecting);

            var farmSecret = serverFinder.GetServerSecret(vpnEndPoint);
            var requestSigner = new ClientRequestSigner(farmSecret);
            var connectorService = new ConnectorService(
                new ConnectorServiceOptions {
                    ProxyConnector = proxyConnector,
                    SocketFactory = socketFactory,
                    VpnEndPoint = vpnEndPoint,
                    RequestTimeout = config.Transport.TcpConnectTimeout,
                    AllowChannelReuse = false,
                    TcpPacketChannelKernelBufferSize = config.Transport.TcpPacketChannelKernelBufferSize
                }, requestSigner);
            requestSender = new RequestSender(connectorService, requestSigner);

            var clientInfo = new ClientInfo {
                ClientId = config.ClientId,
                ClientVersion = config.Version.ToString(3),
                MinProtocolVersion = VpnHoodClientConfig.MinProtocolVersion,
                MaxProtocolVersion = VpnHoodClientConfig.MaxProtocolVersion,
                UserAgent = config.UserAgent
            };

            var helloRequest = new HelloRequest {
                RequestId = UniqueIdFactory.Create(),
                EncryptedClientId = VhUtils.EncryptClientId(clientInfo.ClientId, token.Secret),
                ClientInfo = clientInfo,
                TokenId = token.TokenId,
                ServerLocation = serverFinder.ServerLocation,
                PlanId = config.PlanId,
                AccessCode = config.AccessCode,
                AllowRedirect = allowRedirect,
                IsIpV6Supported = vpnAdapter.IsIpVersionSupported(IpVersion.IPv6),
                UserReview = config.UserReview,
                Mtu = TransportDefaults.MtuClient,
                AllowAnonymousTracker = config.AllowAnonymousTracker
            };

            var requestResult = await SendHello(requestSender, helloRequest, farmSecret, cancellationToken).Vhc();
            _ = requestResult.TryDisposeAsync();
            connectorService.AllowChannelReuse = config.AllowStreamReuse;

            var helloResponse = requestResult.Response;
            // padding rides on the WebSocket masking a server takes only at HTTP 15
            connectorService.InitialPadding.Enable(helloResponse.StartPadding && !connectorService.IsLegacyProtocol);
            if (helloResponse.ProtocolVersion < VpnHoodClientConfig.MinProtocolVersion ||
                helloResponse.ProtocolVersion > VpnHoodClientConfig.MaxProtocolVersion)
                throw new NotSupportedException("The server does not support the requested protocol version.");
            if (connectorService.IsLegacyProtocol)
                connectorService.UseLegacyProtocol(helloResponse.ProtocolVersion);
            if (helloResponse.ClientPublicAddress is null)
                throw new NotSupportedException($"Server must returns {nameof(helloResponse.ClientPublicAddress)}.");

            // a server before the per-version flags sends only this one, and means both IP versions by it
#pragma warning disable CS0618
            if (helloResponse is { IsTcpPacketSupported: true, IsTcpPacketIpV4Supported: false, IsTcpPacketIpV6Supported: false })
                helloResponse.IsTcpPacketIpV4Supported = helloResponse.IsTcpPacketIpV6Supported = true;
#pragma warning restore CS0618

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
                $"QuicPort: {helloResponse.QuicPort}, " +
                $"IsTcpPacketIpV4Supported: {helloResponse.IsTcpPacketIpV4Supported}, " +
                $"IsTcpPacketIpV6Supported: {helloResponse.IsTcpPacketIpV6Supported}, " +
                $"IsTcpForwardingSupported: {helloResponse.IsTcpForwardingSupported}, " +
                $"IsQuicPacketSupported: {helloResponse.IsQuicPacketSupported}, " +
                $"IsUdpPacketSupported: {helloResponse.IsUdpPacketSupported}, " +
                $"IsLocalNetworkAllowed: {serverAllowedLocalNetworks.Any()}, " +
                $"NetworkV4: {helloResponse.VirtualIpNetworkV4}, " +
                $"NetworkV6: {helloResponse.VirtualIpNetworkV6}, " +
                $"ClientCountry: {helloResponse.ClientCountry}, " +
                $"MaxSpeedMbps: {helloResponse.AccessInfo?.MaxSpeedMbps}");

            var sessionId = helloResponse.SessionId;
            var sessionKey = helloResponse.SessionKey;

            // With proxies, the tunnel goes through them alone: UDP and QUIC would go to the server straight, around
            // them, so the session takes neither, as with a server that offers none. By the connector, not by whether it
            // has a proxy at this moment: a managed list may fill while the session runs
            var isProxied = proxyConnector != null;
            var hostUdpEndPoint = !isProxied && ChannelProtocolValidator.IsUdpChannelOffered(helloResponse) &&
                                  helloResponse.UdpPort is { } udpPort
                ? new IPEndPoint(connectorService.VpnEndPoint.TcpEndPoint.Address, udpPort)
                : null;

            var hostQuicEndPoint = !isProxied && helloResponse.QuicPort > 0
                ? new IPEndPoint(connectorService.VpnEndPoint.TcpEndPoint.Address, helloResponse.QuicPort.Value)
                : null;

            connectorService.Init(
                channelIdleTimeout: helloResponse.ChannelIdleTimeout,
                useWebSocket: config.UseWebSocket,
                requestTimeout: helloResponse.RequestTimeout.WhenNoDebugger(),
                useQuic: channelProtocol == ChannelProtocol.Quic && hostQuicEndPoint != null,
                quicEndPoint: hostQuicEndPoint);

            // Give the server layer its word before anything consults the pipe: the full routing
            // declaration (app ∩ adapter — by default the adapter ranges already exclude local networks,
            // so the server's own word rejects a home-LAN resolver, no client-side LAN heuristic involved)
            // and the fate of what it does not route — per family: a v4-only server leaves every IPv6
            // destination to UnsupportedIpV6Mode regardless of its declared ranges. From here one Process
            // call answers for the whole session: client gates first, then the server's ranges. The modes are the
            // client's own, from its options or set since (VpnHoodClient)
            serverIpFilter.IncludeRanges = serverIncludeIpRanges;
            serverIpFilter.IsIpV6SupportedByServer = helloResponse.IsIpV6Supported;

            var dnsConfig = ClientHelper.GetDnsServers(
                config.DnsServers,
                serverDnsAddresses: helloResponse.DnsServers ?? [],
                serverIpFilter: serverIpFilter,
                splitDnsMode: config.SplitDnsMode);

            // What the adapter routes into the tunnel: the whole story — the per-family capture policy
            // (the unsupported-ip modes against the server's adapter declaration), the settled DNS plan
            // (an in-tunnel plan's resolvers survive every exclusion, an out-of-tunnel plan stays
            // outside) and the mechanical exclusions — lives in BuildIncludeIpRangesByDevice.
            var sessionIncludeIpRangesByDevice = ClientHelper.BuildIncludeIpRangesByDevice(
                clientIncludeIpRanges: config.IncludeIpRangesByDevice.ToOrderedList(),
                serverIncludeIpRanges: serverIncludeIpRangesByDevice,
                unroutedIpMode: serverIpFilter.UnroutedIpMode,
                unsupportedIpV6Mode: serverIpFilter.UnsupportedIpV6Mode,
                isIpV6SupportedByServer: helloResponse.IsIpV6Supported,
                includeLocalNetwork: config.IncludeLocalNetwork,
                hostIpAddress: connectorService.VpnEndPoint.TcpEndPoint.Address,
                dnsConfig: dnsConfig);


            if (helloResponse.SuppressedTo == SessionSuppressType.YourSelf)
                VhLogger.Instance.LogWarning("You suppressed a session of yourself!");
            else if (helloResponse.SuppressedTo == SessionSuppressType.Other)
                VhLogger.Instance.LogWarning("You suppressed a session of another client!");

            if (hostUdpEndPoint is null)
                VhLogger.Instance.LogWarning(isProxied
                    ? "The session takes no UDP channel: its connections go through proxies."
                    : "The server does not support UDP channel.");

            ClientHelper.ValidateTcpSupport(helloResponse, useTcpForwarding);

            var sessionInfo = new SessionInfo {
                SessionId = helloResponse.SessionId.ToString(),
                ClientPublicIpAddress = helloResponse.ClientPublicAddress,
                ClientCountry = helloResponse.ClientCountry,
                AccessInfo = helloResponse.AccessInfo ?? new AccessInfo(),
                IsLocalNetworkAllowed = serverAllowedLocalNetworks.Any(),
                DnsConfig = dnsConfig,
                IsTrafficSplitByServer = ClientHelper.IsTrafficSplitByServer(serverIncludeIpRangesByApp,
                    serverIncludeIpRangesByDevice, helloResponse.IsIpV6Supported),
                IsIpV6SupportedByServer = helloResponse.IsIpV6Supported,
                IsPremiumSession = helloResponse.AccessUsage?.IsPremium ?? false,
                IsUdpChannelSupported = hostUdpEndPoint != null,
                AccessKey = helloResponse.AccessKey,
                ServerVersion = Version.Parse(helloResponse.ServerVersion),
                SuppressedTo = helloResponse.SuppressedTo,
                AdRequirement = helloResponse.AdRequirement,
                CreatedTime = FastDateTime.UtcNow,
                IsTcpPacketIpV4Supported = helloResponse.IsTcpPacketIpV4Supported,
                IsTcpPacketIpV6Supported = helloResponse.IsTcpPacketIpV6Supported,
                IsTcpForwardingSupported = helloResponse.IsTcpForwardingSupported,
                IsQuicPacketSupported = helloResponse.IsQuicPacketSupported,
                IsUdpPacketSupported = helloResponse.IsUdpPacketSupported,
                IsQuicChannelSupported = hostQuicEndPoint != null && socketFactory.QuicClientFactory?.IsSupported == true,
                ChannelProtocols = ChannelProtocolValidator.GetChannelProtocols(
                    isUdpChannelSupported: hostUdpEndPoint != null,
                    isQuicChannelSupported: hostQuicEndPoint != null && socketFactory.QuicClientFactory?.IsSupported == true),
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
                        VhLogger.Instance, CancellationToken.None);
                }

                if (tracker != null) {
                    _ = tracker.TryTrack(
                        ClientTrackerBuilder.BuildConnectionSucceeded(
                            serverFinder.ServerLocation,
                            isIpV6Supported: vpnAdapter.IsIpVersionSupported(IpVersion.IPv6),
                            hasRedirected: !allowRedirect,
                            endPoint: connectorService.VpnEndPoint.TcpEndPoint,
                            adNetworkName: null), CancellationToken.None);
                }
            }

            var networkV4 = helloResponse.VirtualIpNetworkV4 ?? new IpNetwork(IPAddress.Parse("10.255.0.2"), 32);
            var networkV6 = helloResponse.VirtualIpNetworkV6 ??
                            new IpNetwork(IPAddressUtil.GenerateUlaAddress(0x1001), 128);
            VhLogger.Instance.LogInformation(
                "Starting VpnAdapter... DnsServers: {DnsServers}, IncludeNetworks: {longIncludeNetworks}",
                sessionInfo.DnsConfig, VhLogger.Format(sessionIncludeIpRangesByDevice.ToIpNetworks()));

            // the tunnel's MTU, which the adapter takes as its own; below 1280 an adapter carries no IPv6
            var mtu = ClientHelper.GetTunnelMtu(helloRequest.Mtu, helloResponse);
            if (mtu < 1280)
                throw new InvalidOperationException($"The server MTU is too small. MTU: {mtu}");

            var adapterOptions = new VpnAdapterOptions {
                ServerIp = connectorService.VpnEndPoint.TcpEndPoint.Address,
                Mtu = mtu,
                DnsServers = dnsConfig.DnsServers,

                // the DNS rule's promise, kept where the host's resolver would ask another interface's servers
                UseExclusiveDns = config.SplitDnsMode is SplitDnsMode.IncludeAll,
                VirtualIpNetworkV4 = networkV4,
                VirtualIpNetworkV6 = networkV6,
                IncludeNetworks = sessionIncludeIpRangesByDevice.ToIpNetworks(),
                SessionName = config.SessionName,
                ExcludeApps = config.ExcludeApps,
                IncludeApps = config.IncludeApps
            };

            var session = new ClientSession(
                options: new ClientSessionOptions {
                    SessionInfo = sessionInfo,
                    VpnAdapter = vpnAdapter,
                    SocketFactory = socketFactory,
                    Tracker = tracker,
                    AccessUsage = helloResponse.AccessUsage ?? new AccessUsage(),
                    RequestSender = requestSender,
                    DomainFilteringService = domainFilteringService,
                    NetFilter = netFilter,
                    ChannelProtocol = channelProtocol,
                    SplitDnsMode = config.SplitDnsMode,
                    DropQuic = dropQuic,
                    DropUdp = dropUdp,
                    UseTcpForwarding = useTcpForwarding,
                    ServerAddressCheck = CreateServerAddressCheck(vpnEndPoint)
                },
                config: new ClientSessionConfig {
                    AdapterOptions = adapterOptions,
                    SessionId = sessionId,
                    SessionKey = sessionKey,
                    Mtu = mtu,
                    MaxSpeedMbps = helloResponse.AccessInfo?.MaxSpeedMbps,
                    MaxPacketChannelLifespan = config.MaxPacketChannelLifespan,
                    MinPacketChannelLifespan = config.MinPacketChannelLifespan,
                    Transport = config.Transport,
                    DnsConfig = dnsConfig,
                    HostTcpEndPoint = connectorService.VpnEndPoint.TcpEndPoint,
                    HostUdpEndPoint = hostUdpEndPoint,
                    HostQuicEndPoint = hostQuicEndPoint,
                    IsIpV6SupportedByServer = helloResponse.IsIpV6Supported,
                    AdRequirement = helloResponse.AdRequirement,
                    MaxPacketChannelCount = helloResponse.MaxPacketChannelCount != 0
                        ? Math.Min(config.Transport.MaxPacketChannelCount, helloResponse.MaxPacketChannelCount)
                        : config.Transport.MaxPacketChannelCount
                });

            return session;
        }
        catch (TimeoutException) {
            CloseRequestSender(requestSender);
            throw new ConnectionTimeoutException("Could not connect to the server in the given time.");
        }
        catch (RedirectHostException ex) {
            CloseRequestSender(requestSender);
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
            CloseRequestSender(requestSender);
            throw;
        }
    }

    // The hello goes the way its server took last, at first HTTP 15 with its proof. A 401 the clock does not explain
    // comes from a server older than 15 as it does from one refusing the proof, so the hello tries the other way
    // once, but not on a server that took 15 before: no 401 talks the app down from the proof there.
    private static async Task<ConnectorRequestResult<HelloResponse>> SendHello(RequestSender requestSender,
        HelloRequest helloRequest, byte[] farmSecret, CancellationToken cancellationToken)
    {
        var connectorService = requestSender.ConnectorService;
        var endPoint = connectorService.VpnEndPoint.TcpEndPoint;
        var wasLegacy = ServerProtocolMemory.IsLegacy(endPoint, farmSecret);
        if (wasLegacy == true)
            connectorService.UseLegacyProtocol(VpnHoodClientConfig.MinProtocolVersion);

        try {
            return await SendHelloAndRecord(requestSender, helloRequest, farmSecret, cancellationToken).Vhc();
        }
        catch (RequestAuthorizationException) when (wasLegacy != false) {
            var refusedVersion = connectorService.ProtocolVersion;
            if (connectorService.IsLegacyProtocol)
                connectorService.UseProofProtocol();
            else
                connectorService.UseLegacyProtocol(VpnHoodClientConfig.MinProtocolVersion);

            VhLogger.Instance.LogInformation(GeneralEventId.Session,
                "The server refused HTTP protocol {RefusedVersion}; trying {ProtocolVersion}. EndPoint: {EndPoint}",
                refusedVersion, connectorService.ProtocolVersion, VhLogger.Format(endPoint));
            return await SendHelloAndRecord(requestSender, helloRequest, farmSecret, cancellationToken).Vhc();
        }
    }

    private static async Task<ConnectorRequestResult<HelloResponse>> SendHelloAndRecord(RequestSender requestSender,
        HelloRequest helloRequest, byte[] farmSecret, CancellationToken cancellationToken)
    {
        var ret = await requestSender.SendRequest<HelloResponse>(helloRequest, cancellationToken).Vhc();
        ServerProtocolMemory.Record(requestSender.ConnectorService.VpnEndPoint.TcpEndPoint, farmSecret,
            requestSender.ConnectorService.IsLegacyProtocol);
        return ret;
    }

    // only for an address the name gave, and an IPv4 one: the name is asked again for IPv4 addresses only
    private ServerAddressCheck? CreateServerAddressCheck(VpnEndPoint vpnEndPoint)
    {
        return serverFinder.IsByHostName(vpnEndPoint) && vpnEndPoint.TcpEndPoint.AddressFamily == AddressFamily.InterNetwork
            ? new ServerAddressCheck(vpnEndPoint.HostName, vpnEndPoint.TcpEndPoint.Address, vpnAdapter)
            : null;
    }

    // not waited for: its connections' closes may wait for their peers, and the connect's error would wait with them
    private static void CloseRequestSender(RequestSender? requestSender)
    {
        _ = requestSender.TryDisposeAsync();
    }
}
