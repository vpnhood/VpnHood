using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.DomainFiltering;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Proxies;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

internal class ClientHost(
    VpnHoodClient vpnHoodClient,
    ISocketFactory socketFactory,
    DomainFilteringService domainFilterService,
    Tunnel tunnel,
    TimeSpan tcpConnectTimeout,
    ProxyManager proxyManager,
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
    private int _processingCount;

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
                // config tcpOrgClient
                var tcpClient = await tcpListener.AcceptTcpClientAsync().Vhc();
                VhUtils.ConfigTcpClient(tcpClient, null, null);
                var tcpConnection = new TcpConnection(tcpClient, isServer: false, connectionName: "app");
                _ = ProcessConnection(tcpConnection, _cancellationTokenSource.Token);
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
    public void ProcessOutgoingPacket(IpPacket ipPacket, bool? isInRange)
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
                var sync = tcpPacket is { Synchronize: true, Acknowledgment: false };
                var syncCustomData = sync && isInRange != null
                    ? new SyncCustomData { IsInIpRange = isInRange.Value }
                    : (SyncCustomData?)null;

                // add to nat if it is sync packet
                var natItem = syncCustomData != null
                    ? _nat.Add(ipPacket, true)
                    : _nat.Get(ipPacket) ??
                      throw new NatEndpointNotFoundException("Could not find outgoing tcp destination in NAT.");

                // set customData
                if (syncCustomData != null)
                    natItem.CustomData = syncCustomData;

                // rewrite packet by changing source/destination address and port
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

    private Task ProcessConnection(IConnection connection, CancellationToken cancellationToken)
    {
        // get original remote from NAT
        var remoteEndPoint = connection.RemoteEndPoint;
        var ipVersion = remoteEndPoint.IpVersion();
        var natItem =
            (NatItemEx?)_nat.Resolve(ipVersion, IpProtocol.Tcp, (ushort)remoteEndPoint.Port) ??
            throw new Exception(
                $"Could not resolve original remote from NAT! RemotePort: {remoteEndPoint.Port}");

        // check invalid income
        var catcherAddress = ipVersion == IpVersion.IPv4 ? CatcherAddressIpV4 : CatcherAddressIpV6;
        if (!Equals(connection.RemoteEndPoint.Address, catcherAddress))
            throw new Exception("TcpProxy rejected an outbound connection!");

        var syncCustomData = natItem.CustomData as SyncCustomData?;
        var isInRange = syncCustomData?.IsInIpRange ?? true; // default to true if no custom data
        var hostEndPoint = new IPEndPoint(natItem.DestinationAddress, natItem.DestinationPort);
        return ProcessConnection(connection, hostEndPoint, isInRange, cancellationToken);
    }

    private async Task ProcessConnection(
        IConnection connection, IPEndPoint hostEndPoint, bool isInRange, CancellationToken cancellationToken)
    {
        try {
            // check cancellation
            Interlocked.Increment(ref _processingCount);
            cancellationToken.ThrowIfCancellationRequested();

            // create a scope for the logger
            VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel,
                "New TcpProxy Request. LocalPort: {LocalPort}, HostEp: {RemoteEp}",
                connection.RemoteEndPoint.Port, hostEndPoint);

            // Apply SNI filtering and update connection if needed
            (connection, isInRange) = await ApplySniFiltering(connection, hostEndPoint, isInRange, cancellationToken).Vhc();

            // Filter by IP
            if (isInRange) {
                // Create and add to tunnel channel
                await AddTunnelChannel(connection, hostEndPoint, cancellationToken).Vhc();
                _stat.TcpTunnelledCount++;
            }
            else {
                // Create and add to exclude channel
                await AddPassthruChannel(connection, hostEndPoint, cancellationToken).Vhc();
                _stat.TcpPassthruCount++;
            }

        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.ProxyChannel, ex, "Could not handle a tcp stream request.");
            await connection.DisposeAsync();
        }
        finally {
            Interlocked.Decrement(ref _processingCount);
        }
    }

    private async Task AddPassthruChannel(IConnection connection, IPEndPoint hostEndPoint, CancellationToken cancellationToken)
    {
        if (hostEndPoint.IsV6() && vpnHoodClient.SessionStatus?.IsIpV6SupportedByClient is null or false)
            throw new Exception("IPv6 is not supported by client.");

        //log
        VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel,
            "Adding a new stream channel to bypass. HostEp: {HostEp}, ConnectionId: {ConnectionId}",
            VhLogger.Format(hostEndPoint), connection.ConnectionId);

        // set timeout
        using var timeoutCts = new CancellationTokenSource(tcpConnectTimeout);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        // connect to host
        var tcpClient = socketFactory.CreateTcpClient(hostEndPoint);
        await tcpClient.ConnectAsync(hostEndPoint.Address, hostEndPoint.Port, connectCts.Token).Vhc();
        var hostConnection = new TcpConnection(tcpClient, connectionId: connection.ConnectionId, connectionName: "host", isServer: false);

        try {
            // create and add the channel
            var channel = new ProxyChannel(hostConnection.ToString(), connection, hostConnection, StreamProxyBufferSize);
            proxyManager.AddChannel(channel, disposeOnFail: true );
        }
        catch {
            await hostConnection.DisposeAsync();
            throw;
        }
    }

    private async Task AddTunnelChannel(
        IConnection connection, IPEndPoint hostEndPoint, CancellationToken cancellationToken)
    {
        if (hostEndPoint.IsV6() && vpnHoodClient.SessionStatus?.IsIpV6SupportedByServer is null or false)
            throw new Exception("IPv6 is not supported by server.");

        //log
        VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel,
            "Adding a new stream channel to tunnel. HostEp: {HostEp}, ConnectionId: {ConnectionId}",
            VhLogger.Format(hostEndPoint), connection.ConnectionId);


        //handle small buffer for tiny TLS hello or small HTTP request to remove bidirectional pattern
        var memory = new Memory<byte>(new byte[TunnelDefaults.PrefetchStreamBufferSize]);
        var read = await connection.Stream.ReadAsync(memory, cancellationToken);
        var initContents = memory[..read];

        // Create the Request
        var request = new StreamProxyChannelRequest {
            RequestId = connection.ConnectionId,
            SessionId = vpnHoodClient.SessionId,
            SessionKey = vpnHoodClient.SessionKey,
            DestinationEndPoint = hostEndPoint
        };

        // read the response
        var requestEx = new ClientRequestEx { Request = request, PostBuffer = initContents };
        var requestResult = await vpnHoodClient.SendRequest<SessionResponse>(requestEx, cancellationToken).Vhc();
        try {
            var proxyConnection = requestResult.Connection;

            // add stream proxy
            var channel = new ProxyChannel(proxyConnection.ToString()!, connection, proxyConnection,
                streamProxyBufferSize);
            tunnel.AddChannel(channel, disposeIfFailed: true);
        }
        catch {
            requestResult.Dispose();
            throw;
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

    private async Task<(IConnection Connection, bool IsInRange)> ApplySniFiltering(
        IConnection connection, IPEndPoint hostEndPoint, bool isInRange, CancellationToken cancellationToken)
    {
        // Filter by SNI
        var filterResult = await domainFilterService.ProcessStream(
            connection.Stream, hostEndPoint, cancellationToken).Vhc();

        switch (filterResult.Action) {
            case DomainFilterAction.Block:
                VhLogger.Instance.LogInformation(GeneralEventId.Sni,
                    "Domain has been blocked. Domain: {Domain}",
                    VhLogger.FormatHostName(filterResult.DomainName));

                throw new Exception($"Domain has been blocked. Domain: {filterResult.DomainName}");

            case DomainFilterAction.Exclude:
                VhLogger.Instance.LogDebug(GeneralEventId.Sni,
                    "Domain has been excluded from VPN. Domain: {Domain}",
                    VhLogger.FormatHostName(filterResult.DomainName));
                isInRange = false;
                break;

            case DomainFilterAction.Include:
                VhLogger.Instance.LogDebug(GeneralEventId.Sni,
                    "Domain has been included in VPN. Domain: {Domain}",
                    VhLogger.FormatHostName(filterResult.DomainName));
                isInRange = true;
                break;

            case DomainFilterAction.None:
            default:
                // default isInRange
                break;
        }

        // update connection stream with ReadBufferedStream to pre-append the read data
        if (filterResult.ReadData.Length > 0)
            connection = new ConnectionDecorator(connection,
                new ReadBufferedStream(connection.Stream, leaveOpen: false, filterResult.ReadData.Span) {
                    AllowBufferRefill = false
                });

        return (connection, isInRange);
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