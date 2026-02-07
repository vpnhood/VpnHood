using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.SniFiltering;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

internal class ClientHost2(
    VpnHoodClient vpnHoodClient,
    DomainFilterService domainFilterService,
    Tunnel tunnel,
    IPAddress catcherAddressIpV4,
    IPAddress catcherAddressIpV6,
    TransferBufferSize streamProxyBufferSize)
    : IDisposable
{
    private bool _disposed;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ClientHostStat _stat = new();
    private readonly Nat _nat = new(true);
    private TcpListener? _tcpListenerIpV4;
    private TcpListener? _tcpListenerIpV6;
    private IPEndPoint? _localEndpointIpV4;
    private IPEndPoint? _localEndpointIpV6;

    public IPAddress CatcherAddressIpV4 => catcherAddressIpV4;
    public IPAddress CatcherAddressIpV6 => catcherAddressIpV6;
    public TransferBufferSize StreamProxyBufferSize => streamProxyBufferSize;
    public IClientHostStat Stat => _stat;
    public event EventHandler<IpPacket>? PacketReceived;

    public void DropCurrentConnections()
    {
        _nat.RemoveAll();
    }

    public bool IsOwnPacket(IpPacket ipPacket)
    {
        if (ipPacket.Protocol != IpProtocol.Tcp)
            return false;

        return CatcherAddressIpV4.SpanEquals(ipPacket.DestinationAddressSpan) ||
               CatcherAddressIpV6.SpanEquals(ipPacket.DestinationAddressSpan);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var logScope = VhLogger.Instance.BeginScope("ClientHost");
        VhLogger.Instance.LogInformation("Starting ClientHost...");

        // IpV4
        _tcpListenerIpV4 = new TcpListener(IPAddress.Any, 0);
        _tcpListenerIpV4.Start();
        _localEndpointIpV4 = (IPEndPoint)_tcpListenerIpV4.LocalEndpoint; //it is slow; make sure to cache it
        VhLogger.Instance.LogInformation("ClientHost is listening. EndPoint: {EndPoint}",
            VhLogger.Format(_localEndpointIpV4));
        _ = AcceptTcpClientLoop(_tcpListenerIpV4);

        // IpV6
        try {
            _tcpListenerIpV6 = new TcpListener(IPAddress.IPv6Any, 0);
            _tcpListenerIpV6.Start();
            _localEndpointIpV6 = (IPEndPoint)_tcpListenerIpV6.LocalEndpoint; //it is slow; make sure to cache it
            VhLogger.Instance.LogInformation("ClientHost is listening. EndPoint: {EndPoint}",
                VhLogger.Format(_localEndpointIpV6));
            _ = AcceptTcpClientLoop(_tcpListenerIpV6);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex,
                "Could not create a listener. EndPoint: {EndPoint}",
                VhLogger.Format(new IPEndPoint(IPAddress.IPv6Any, 0)));
        }

        // check available memory
    }

    private async Task AcceptTcpClientLoop(TcpListener tcpListener)
    {
        var localEp = (IPEndPoint)tcpListener.LocalEndpoint;

        try {
            while (!_cancellationTokenSource.IsCancellationRequested) {
                var tcpClient = await tcpListener.AcceptTcpClientAsync().Vhc();
                _ = ProcessClient(tcpClient, _cancellationTokenSource.Token);
            }
        }
        catch (Exception ex) {
            if (!_disposed)
                VhLogger.LogError(GeneralEventId.Request, ex, "");
        }
        finally {
            VhLogger.Instance.LogInformation("ClientHost Listener has been closed. LocalEp: {localEp}", localEp);
        }
    }

    // this method should not be called in multi-thread, the return buffer is shared and will be modified on next call
    public void ProcessOutgoingPacket(IpPacket ipPacket)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PacketLogger.LogPacket(ipPacket, "Processing a ClientHost packet...");

        // check packet type
        if (_localEndpointIpV4 == null)
            throw new InvalidOperationException(
                $"{nameof(_localEndpointIpV4)} has not been initialized! Did you call Start!");

        var catcherAddress = ipPacket.Version == IpVersion.IPv4 ? CatcherAddressIpV4 : CatcherAddressIpV6;
        var localEndPoint = ipPacket.Version == IpVersion.IPv4 ? _localEndpointIpV4 : _localEndpointIpV6;

        try {
            var tcpPacket = ipPacket.ExtractTcp();

            // check local endpoint
            if (localEndPoint == null)
                throw new Exception("There is no localEndPoint registered for this packet.");

            // redirect to inbound
            if (catcherAddress.SpanEquals(ipPacket.DestinationAddressSpan)) {
                var natItem = (NatItemEx?)_nat.Resolve(ipPacket.Version, ipPacket.Protocol, tcpPacket.DestinationPort)
                              ?? throw new NatEndpointNotFoundException(
                                  "Could not find incoming tcp destination in NAT.");

                ipPacket.SourceAddress = natItem.DestinationAddress;
                ipPacket.DestinationAddress = natItem.SourceAddress;
                tcpPacket.SourcePort = natItem.DestinationPort;
                tcpPacket.DestinationPort = natItem.SourcePort;
            }

            // Redirect outbound to the local address
            else {
                var syncCustomData = ProcessOutgoingSyncPacket(ipPacket, tcpPacket);

                // add to nat if it is sync packet
                var natItem = syncCustomData != null
                    ? _nat.Add(ipPacket, true)
                    : _nat.Get(ipPacket) ??
                      throw new NatEndpointNotFoundException("Could not find outgoing tcp destination in NAT.");

                // set customData
                if (syncCustomData != null)
                    natItem.CustomData = syncCustomData;

                tcpPacket.SourcePort = natItem.NatId; // 1
                ipPacket.DestinationAddress = ipPacket.SourceAddress; // 2
                ipPacket.SourceAddress = catcherAddress; //3
                tcpPacket.DestinationPort = (ushort)localEndPoint.Port; //4
            }

            ipPacket.UpdateAllChecksums();
            PacketReceived?.Invoke(this, ipPacket);
        }
        catch (NatEndpointNotFoundException ex) when (ipPacket.Protocol == IpProtocol.Tcp) {
            var resultPacket = PacketBuilder.BuildTcpResetReply(ipPacket);
            PacketReceived?.Invoke(this, resultPacket);
            throw new PacketDropException("Packet dropped and TCP reset sent.", ex);
        }
    }

    private SyncCustomData? ProcessOutgoingSyncPacket(IpPacket ipPacket, TcpPacket tcpPacket)
    {
        var sync = tcpPacket is { Synchronize: true, Acknowledgment: false };
        if (!sync)
            return null;

        var syncCustomData = new SyncCustomData {
            IsInIpRange = vpnHoodClient.IsInEpRange(ipPacket)
        };

        if (ipPacket.Version == IpVersion.IPv6)
            ProcessOutgoingSyncIpV6Packet(syncCustomData);

        return syncCustomData;
    }

    private void ProcessOutgoingSyncIpV6Packet(SyncCustomData syncCustomData)
    {
        if (domainFilterService.IsEnabled &&
            (!vpnHoodClient.IsIpV6SupportedByServer || !vpnHoodClient.IsIpV6SupportedByClient))
            throw new Exception("DomainFilter is on but IPv6 is not fully supported.");

        if (syncCustomData.IsInIpRange && !vpnHoodClient.IsIpV6SupportedByServer)
            throw new Exception("IPv6 is not supported by the server.");

        if (!syncCustomData.IsInIpRange && !vpnHoodClient.IsIpV6SupportedByClient)
            throw new Exception("IPv6 is not supported by the client.");
    }

    private Task AddToPassthrough(IConnection connection, CancellationToken cancellationToken)
    {
        var channelId = UniqueIdFactory.Create() + ":client:passthrough";
        vpnHoodClient.AddPassthruTcpStream(
                new ReusableConnection(null, connection.Stream, channelId + ":tunnel"),
                connection.RemoteEndPoint,
                channelId, filterResult.ReadData, cancellationToken).Vhc();

        _stat.TcpPassthruCount++;
        return Task.CompletedTask;
    }

    private async Task AddToTunnel(IConnection connection, CancellationToken cancellationToken)
    {
        // Create the Request
        var request = new StreamProxyChannelRequest {
            RequestId = UniqueIdFactory.Create(),
            SessionId = vpnHoodClient.SessionId,
            SessionKey = vpnHoodClient.SessionKey,
            DestinationEndPoint = connection.RemoteEndPoint
        };

        // read the response
        var requestResult = await vpnHoodClient.SendRequest<SessionResponse>(request, cancellationToken).Vhc();
        var proxyConnection = requestResult.Connection;

        // create a ProxyChannel
        VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel,
            "Adding a channel to session. SessionId: {SessionId}...", VhLogger.FormatId(request.SessionId));
        var orgTcpConnection = 
            new ReusableConnection(null, connection.Stream,
            proxyConnection.ConnectionId.Replace(":tunnel", ":app"));

        // add stream proxy
        var channel = new ProxyChannel(request.RequestId, orgTcpConnection, proxyConnection, streamProxyBufferSize);
        tunnel.AddChannel(channel);
        _stat.TcpTunnelledCount++;
    }

    private async Task ProcessClient(IConnection connection, bool filterDomain, CancellationToken cancellationToken)
    {
        ConnectorRequestResult<SessionResponse>? requestResult = null;
        ProxyChannel? channel = null;
        var ipVersion = IpVersion.IPv4;

        try {
            // check cancellation
            cancellationToken.ThrowIfCancellationRequested();

            // config tcpOrgClient
            VhUtils.ConfigTcpClient(connection, null, null);
            // check invalid income
            var catcherAddress = ipVersion == IpVersion.IPv4 ? CatcherAddressIpV4 : CatcherAddressIpV6;
            if (!Equals(orgRemoteEndPoint.Address, catcherAddress))
                throw new Exception("TcpProxy rejected an outbound connection!");

            // create a scope for the logger
            using var scope = VhLogger.Instance.BeginScope("LocalPort: {LocalPort}, RemoteEp: {RemoteEp}",
                connection.LocalEndPoint.Port, VhLogger.Format(connection.RemoteEndPoint));
            VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel, "New TcpProxy Request.");

            // Filter by SNI
            var filterResult = await domainFilterService
                .Process(connection.Stream, connection.RemoteEndPoint.Address, cancellationToken)
                .Vhc();

            if (filterResult.Action == SniFilterAction.Block) {
                VhLogger.Instance.LogInformation(GeneralEventId.Sni,
                    "Domain has been blocked. Domain: {Domain}",
                    VhLogger.FormatHostName(filterResult.DomainName));

                throw new Exception($"Domain has been blocked. Domain: {filterResult.DomainName}");
            }

            // Filter by IP
            var isInIpRange = syncCustomData?.IsInIpRange ??
                              vpnHoodClient.IsInEpRange(natItem.DestinationAddress, natItem.DestinationPort);
            if (filterResult.Action == SniFilterAction.Exclude ||
                (!isInIpRange && filterResult.Action != SniFilterAction.Include)) {
                var channelId = UniqueIdFactory.Create() + ":client:passthrough";
                await vpnHoodClient.AddPassthruTcpStream(
                        new ReusableConnection(connection, connection.GetStream(), channelId + ":tunnel"),
                        new IPEndPoint(natItem.DestinationAddress, natItem.DestinationPort),
                        channelId, filterResult.ReadData, cancellationToken)
                    .Vhc();

                _stat.TcpPassthruCount++;
                return;
            }

            // Create the Request
            var request = new StreamProxyChannelRequest {
                RequestId = UniqueIdFactory.Create(),
                SessionId = vpnHoodClient.SessionId,
                SessionKey = vpnHoodClient.SessionKey,
                DestinationEndPoint = new IPEndPoint(natItem.DestinationAddress, natItem.DestinationPort)
            };

            // read the response
            requestResult = await vpnHoodClient.SendRequest<SessionResponse>(request, cancellationToken).Vhc();
            var proxyConnection = requestResult.Connection;

            // create a ProxyChannel
            VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel,
                "Adding a channel to session. SessionId: {SessionId}...", VhLogger.FormatId(request.SessionId));
            var orgTcpConnection =
                new ReusableConnection(connection, connection.GetStream(),
                    proxyConnection.ConnectionId.Replace(":tunnel", ":app"));

            // flush initBuffer
            await proxyConnection.Stream.WriteAsync(filterResult.ReadData, cancellationToken);

            // add stream proxy
            channel = new ProxyChannel(request.RequestId, orgTcpConnection, proxyConnection, streamProxyBufferSize);
            tunnel.AddChannel(channel);
            _stat.TcpTunnelledCount++;
        }
        catch (Exception ex) {
            // disable IPv6 if detect the new network does not have IpV6
            if (ipVersion == IpVersion.IPv6 &&
                ex is SocketException { SocketErrorCode: SocketError.NetworkUnreachable })
                vpnHoodClient.IsIpV6SupportedByClient = false;

            channel?.Dispose();
            requestResult?.Dispose();
            connection.Dispose();
            VhLogger.LogError(GeneralEventId.ProxyChannel, ex, "");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _cancellationTokenSource.TryCancel();
        _cancellationTokenSource.Dispose();

        _tcpListenerIpV4?.Stop();
        _tcpListenerIpV6?.Stop();
        _nat.Dispose();
        PacketReceived = null;

        _disposed = true;
    }

    private class ClientHostStat : IClientHostStat
    {
        public int TcpTunnelledCount { get; set; }
        public int TcpPassthruCount { get; set; }
    }

    public struct SyncCustomData
    {
        public required bool IsInIpRange { get; init; }
    }
}