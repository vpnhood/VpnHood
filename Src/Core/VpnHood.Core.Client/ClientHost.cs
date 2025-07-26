using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Client.DomainFiltering;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.Exceptions;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

internal class ClientHost(
    VpnHoodClient vpnHoodClient,
    Tunnel tunnel,
    IPAddress catcherAddressIpV4,
    IPAddress catcherAddressIpV6,
    TransferBufferSize streamProxyBufferSize)
    : IDisposable
{
    private bool _disposed;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private TcpListener? _tcpListenerIpV4;
    private TcpListener? _tcpListenerIpV6;
    private IPEndPoint? _localEndpointIpV4;
    private IPEndPoint? _localEndpointIpV6;
    private int _processingCount;
    private readonly ClientHostStat _stat = new();
    private readonly Nat _nat = new(true);

    public IPAddress CatcherAddressIpV4 => catcherAddressIpV4;
    public IPAddress CatcherAddressIpV6 => catcherAddressIpV6;
    public TransferBufferSize StreamProxyBufferSize => streamProxyBufferSize;
    public IClientHostStat Stat => _stat;
    public event EventHandler<IpPacket>? PacketReceived;

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
        VhLogger.Instance.LogInformation("ClientHost is listening. EndPoint: {EndPoint}", VhLogger.Format(_localEndpointIpV4));
        _ = AcceptTcpClientLoop(_tcpListenerIpV4);

        // IpV6
        try {
            _tcpListenerIpV6 = new TcpListener(IPAddress.IPv6Any, 0);
            _tcpListenerIpV6.Start();
            _localEndpointIpV6 = (IPEndPoint)_tcpListenerIpV6.LocalEndpoint; //it is slow; make sure to cache it
            VhLogger.Instance.LogInformation("ClientHost is listening. EndPoint: {EndPoint}", VhLogger.Format(_localEndpointIpV6));
            _ = AcceptTcpClientLoop(_tcpListenerIpV6);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex,
                "Could not create a listener. EndPoint: {EndPoint}", VhLogger.Format(new IPEndPoint(IPAddress.IPv6Any, 0)));
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
                VhLogger.LogError(GeneralEventId.Tcp, ex, "");
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
                              ?? throw new NatEndpointNotFoundException("Could not find incoming tcp destination in NAT.");

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
            IsInIpRange = vpnHoodClient.IsInIpRange(ipPacket.DestinationAddress)
        };

        if (ipPacket.Version == IpVersion.IPv6)
            ProcessOutgoingSyncIpV6Packet(syncCustomData);

        return syncCustomData;
    }

    private void ProcessOutgoingSyncIpV6Packet(SyncCustomData syncCustomData)
    {
        if (vpnHoodClient.DomainFilterService.IsEnabled &&
            (!vpnHoodClient.IsIpV6SupportedByServer || !vpnHoodClient.IsIpV6SupportedByClient))
            throw new Exception("DomainFilter is on but IPv6 is not fully supported.");

        if (syncCustomData.IsInIpRange && !vpnHoodClient.IsIpV6SupportedByServer)
            throw new Exception("IPv6 is not supported by the server.");

        if (!syncCustomData.IsInIpRange && !vpnHoodClient.IsIpV6SupportedByClient)
            throw new Exception("IPv6 is not supported by the client.");
    }

    private async Task ProcessClient(TcpClient orgTcpClient, CancellationToken cancellationToken)
    {
        if (orgTcpClient is null) throw new ArgumentNullException(nameof(orgTcpClient));
        ConnectorRequestResult<SessionResponse>? requestResult = null;
        ProxyChannel? channel = null;
        var ipVersion = IpVersion.IPv4;

        try {
            // check cancellation
            Interlocked.Increment(ref _processingCount);
            cancellationToken.ThrowIfCancellationRequested();

            // config tcpOrgClient
            VhUtils.ConfigTcpClient(orgTcpClient, null, null);

            // get original remote from NAT
            var orgRemoteEndPoint = (IPEndPoint?)orgTcpClient.Client.RemoteEndPoint ??
                                    throw new Exception("Could not get original remote endpoint from TcpClient.");

            ipVersion = orgRemoteEndPoint.IpVersion();
            var natItem =
                (NatItemEx?)_nat.Resolve(ipVersion, IpProtocol.Tcp, (ushort)orgRemoteEndPoint.Port) ??
                throw new Exception(
                    $"Could not resolve original remote from NAT! RemoteEndPoint: {VhLogger.Format(orgTcpClient.Client.RemoteEndPoint)}");

            var syncCustomData = natItem.CustomData as SyncCustomData?;

            // create a scope for the logger
            using var scope = VhLogger.Instance.BeginScope("LocalPort: {LocalPort}, RemoteEp: {RemoteEp}",
                natItem.SourcePort, VhLogger.Format(natItem.DestinationAddress) + ":" + natItem.DestinationPort);
            VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel, "New TcpProxy Request.");

            // check invalid income
            var catcherAddress = ipVersion == IpVersion.IPv4 ? CatcherAddressIpV4 : CatcherAddressIpV6;
            if (!Equals(orgRemoteEndPoint.Address, catcherAddress))
                throw new Exception("TcpProxy rejected an outbound connection!");

            // Filter by SNI
            var filterResult = await vpnHoodClient.DomainFilterService
                .Process(orgTcpClient.GetStream(), natItem.DestinationAddress, cancellationToken)
                .Vhc();

            if (filterResult.Action == DomainFilterAction.Block) {
                VhLogger.Instance.LogInformation(GeneralEventId.Sni,
                    "Domain has been blocked. Domain: {Domain}",
                    VhLogger.FormatHostName(filterResult.DomainName));

                throw new Exception($"Domain has been blocked. Domain: {filterResult.DomainName}");
            }

            // Filter by IP
            var isInIpRange = syncCustomData?.IsInIpRange ?? vpnHoodClient.IsInIpRange(natItem.DestinationAddress);
            if (filterResult.Action == DomainFilterAction.Exclude ||
                (!isInIpRange && filterResult.Action != DomainFilterAction.Include)) {
                var channelId = UniqueIdFactory.Create() + ":client:passthrough";
                await vpnHoodClient.AddPassthruTcpStream(
                        new TcpClientStream(orgTcpClient, orgTcpClient.GetStream(), channelId + ":tunnel"),
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
            var proxyClientStream = requestResult.ClientStream;

            // create a ProxyChannel
            VhLogger.Instance.LogDebug(GeneralEventId.ProxyChannel,
                "Adding a channel to session. SessionId: {SessionId}...", VhLogger.FormatId(request.SessionId));
            var orgTcpClientStream =
                new TcpClientStream(orgTcpClient, orgTcpClient.GetStream(), proxyClientStream.ClientStreamId.Replace(":tunnel", ":app"));

            // flush initBuffer
            await proxyClientStream.Stream.WriteAsync(filterResult.ReadData, cancellationToken);

            // add stream proxy
            channel = new ProxyChannel(request.RequestId, orgTcpClientStream, proxyClientStream, streamProxyBufferSize);
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
            orgTcpClient.Dispose();
            VhLogger.LogError(GeneralEventId.ProxyChannel, ex, "");
        }
        finally {
            Interlocked.Decrement(ref _processingCount);
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