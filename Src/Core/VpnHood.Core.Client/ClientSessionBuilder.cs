using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Client.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;
//todo
/*
internal class ClientSessionBuilder(
    Token token,
    VpnHoodClientConfig config,
    ISocketFactory socketFactory,
    ProxyEndPointManager proxyEndPointManager,
    ServerFinder serverFinder,
    StaticIpFilter staticIpFilter,
    IReadOnlyList<IPAddress> catcherAddressIps,
    bool canProtectSocket)
{
    public record BuildResult(ClientSession Session, HelloResponse HelloResponse);

    public async Task<BuildResult> ConnectAsync(
        bool isIpV6SupportedByClient,
        Action<ClientState> onStateChanged,
        CancellationToken cancellationToken)
    {
        // validate proxy servers
        if (proxyEndPointManager.IsEnabled) {
            onStateChanged(ClientState.ValidatingProxies);
            await proxyEndPointManager.CheckServers(cancellationToken).Vhc();

            if (!proxyEndPointManager.Status.IsAnySucceeded)
                throw new UnreachableProxyServerException();
        }

        // find server and build session
        onStateChanged(ClientState.FindingReachableServer);
        var vpnEndPoint = await serverFinder.FindReachableServerAsync([token.ServerToken], cancellationToken).Vhc();
        var allowRedirect = !serverFinder.CustomServerEndpoints.Any();

        return await Build(
            vpnEndPoint,
            allowRedirect: allowRedirect,
            isIpV6SupportedByClient: isIpV6SupportedByClient,
            onStateChanged: onStateChanged,
            cancellationToken).Vhc();
    }

    public async Task<BuildResult> Build(
        VpnEndPoint vpnEndPoint,
        bool allowRedirect,
        bool isIpV6SupportedByClient,
        Action<ClientState>? onStateChanged,
        CancellationToken cancellationToken)
    {
        ConnectorService? connectorService = null;
        try {
            VhLogger.Instance.LogInformation("Connecting to the server... EndPoint: {hostEndPoint}",
                VhLogger.Format(vpnEndPoint.TcpEndPoint));
            onStateChanged?.Invoke(ClientState.Connecting);

            // create connector service
            connectorService = new ConnectorService(
                options: new ConnectorServiceOptions(
                    ProxyEndPointManager: proxyEndPointManager,
                    SocketFactory: socketFactory,
                    VpnEndPoint: vpnEndPoint,
                    RequestTimeout: config.TcpConnectTimeout,
                    AllowTcpReuse: false));

            // send hello request
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
                IsIpV6Supported = isIpV6SupportedByClient,
                UserReview = config.UserReview
            };

            using var requestResult = await connectorService
                .SendRequest<HelloResponse>(request, cancellationToken).Vhc();
            if (requestResult.Connection is ReusableConnection reusableConnection)
                reusableConnection.PreventReuse();
            connectorService.AllowTcpReuse = config.AllowTcpReuse;

            var helloResponse = requestResult.Response;
            if (helloResponse.ClientPublicAddress is null)
                throw new NotSupportedException($"Server must returns {nameof(helloResponse.ClientPublicAddress)}.");

            // sort out server IncludeIpRanges
            var serverIncludeIpRangesByApp = helloResponse.IncludeIpRanges?.ToOrderedList() ?? IpNetwork.All.ToIpRanges();
            var serverIncludeIpRangesByDevice = helloResponse.VpnAdapterIncludeIpRanges?.ToOrderedList() ?? IpNetwork.All.ToIpRanges();
            var serverIncludeIpRanges = serverIncludeIpRangesByApp.Intersect(serverIncludeIpRangesByDevice);
            var serverAllowedLocalNetworks = IpNetwork.LocalNetworks.ToIpRanges().Intersect(serverIncludeIpRanges);

            // log response
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

            // initialize the connector
            connectorService.Init(
                helloResponse.ProtocolVersion,
                requestTimeout: helloResponse.RequestTimeout.WhenNoDebugger(),
                tcpReuseTimeout: helloResponse.TcpReuseTimeout,
                serverSecret: helloResponse.ServerSecret,
                useWebSocket: config.UseWebSocket);

            // determine UDP endpoint
            var hostUdpEndPoint = helloResponse is { UdpPort: > 0, ProtocolVersion: >= 11 }
                ? new IPEndPoint(connectorService.VpnEndPoint.TcpEndPoint.Address, helloResponse.UdpPort.Value)
                : null;

            // build session IP ranges
            staticIpFilter.IncludeRanges = config.IncludeIpRangesByApp.ToOrderedList();

            // set DNS after setting IpFilters
            var dnsStatus = ClientHelper
                .GetDnsServers(config.DnsServers,
                serverDnsAddresses: helloResponse.DnsServers ?? [],
                serverIncludeIpRanges: serverIncludeIpRangesByApp,
                ipFilter: staticIpFilter);

            // build VPN adapter IP ranges
            var sessionIncludeIpRangesByDevice = ClientHelper.BuildIncludeIpRangesByDevice(
                includeIpRanges: serverIncludeIpRangesByDevice.Intersect(config.IncludeIpRangesByDevice),
                canProtectSocket: canProtectSocket,
                includeLocalNetwork: config.IncludeLocalNetwork,
                catcherIps: catcherAddressIps,
                hostIpAddress: connectorService.VpnEndPoint.TcpEndPoint.Address);

            // add serverIncludeIpRanges to includes after determining DnsServers
            // sometimes packet goes directly to the adapter especially on windows, so we need to make sure filter them
            staticIpFilter.IncludeRanges = staticIpFilter.IncludeRanges
                .Intersect(serverIncludeIpRanges)
                .Intersect(sessionIncludeIpRangesByDevice);

            // report Suppressed
            if (helloResponse.SuppressedTo == SessionSuppressType.YourSelf)
                VhLogger.Instance.LogWarning("You suppressed a session of yourself!");

            else if (helloResponse.SuppressedTo == SessionSuppressType.Other)
                VhLogger.Instance.LogWarning("You suppressed a session of another client!");

            // validate channel protocols
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

            // create SessionInfo
            var sessionInfo = new SessionInfo {
                SessionId = helloResponse.SessionId.ToString(),
                ClientPublicIpAddress = helloResponse.ClientPublicAddress,
                ClientCountry = helloResponse.ClientCountry,
                AccessInfo = helloResponse.AccessInfo ?? new AccessInfo(),
                IsLocalNetworkAllowed = serverAllowedLocalNetworks.Any(),
                DnsStatus = dnsStatus,
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

            // create and return session
            return new BuildResult(
                Session: new ClientSession {
                    SessionId = helloResponse.SessionId,
                    SessionKey = helloResponse.SessionKey,
                    ServerSecret = helloResponse.ServerSecret,
                    SessionInfo = sessionInfo,
                    DnsStatus = dnsStatus,
                    HostUdpEndPoint = hostUdpEndPoint,
                    IncludeIpRangesByDevice = sessionIncludeIpRangesByDevice,
                    ConnectorService = connectorService
                },
                HelloResponse: helloResponse);
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

            onStateChanged?.Invoke(ClientState.FindingBestServer);
            var redirectServerTokens = ex.RedirectServerTokens;

            // legacy: convert the redirect host endpoints to a new server token using initial server token
#pragma warning disable CS0618 // Type or member is obsolete
            if (redirectServerTokens is null) {
                var serverToken = JsonUtils.JsonClone(token.ServerToken);
                serverToken.HostEndPoints = ex.RedirectHostEndPoints;
                redirectServerTokens = [serverToken];
            }
#pragma warning restore CS0618 // Type or member is obsolete

            // find the best redirected server
            var redirectedEndPoint =
                await serverFinder.FindBestRedirectedServerAsync(redirectServerTokens, cancellationToken);
            return await Build(redirectedEndPoint, false, isIpV6SupportedByClient, onStateChanged, cancellationToken).Vhc();
        }
        catch {
            connectorService?.Dispose();
            throw;
        }
    }
}
*/