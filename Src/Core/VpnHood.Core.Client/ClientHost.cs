using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Client.DomainFiltering;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Core.Client;

internal class ClientHost(
    VpnHoodClient vpnHoodClient,
    IPAddress catcherAddressIpV4,
    IPAddress catcherAddressIpV6)
    : IAsyncDisposable
{
    private bool _disposed;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<IPPacket> _ipPackets = [];
    private TcpListener? _tcpListenerIpV4;
    private TcpListener? _tcpListenerIpV6;
    private IPEndPoint? _localEndpointIpV4;
    private IPEndPoint? _localEndpointIpV6;
    private int _processingCount;
    private readonly ClientHostStat _stat = new();
    private int _passthruInProcessPacketsCounter;


    public IPAddress CatcherAddressIpV4 { get; } = catcherAddressIpV4;
    public IPAddress CatcherAddressIpV6 { get; } = catcherAddressIpV6;
    public bool IsPassthruInProcessPacketsEnabled => _passthruInProcessPacketsCounter > 0;
    public IClientHostStat Stat => _stat;

    public void EnablePassthruInProcessPackets(bool value)
    {
        if (value)
            Interlocked.Increment(ref _passthruInProcessPacketsCounter);
        else
            Interlocked.Decrement(ref _passthruInProcessPacketsCounter);
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ClientHost));
        using var logScope = VhLogger.Instance.BeginScope("ClientHost");
        VhLogger.Instance.LogInformation("Starting ClientHost...");

        // IpV4
        _tcpListenerIpV4 = new TcpListener(IPAddress.Any, 0);
        _tcpListenerIpV4.Start();
        _localEndpointIpV4 = (IPEndPoint)_tcpListenerIpV4.LocalEndpoint; //it is slow; make sure to cache it
        VhLogger.Instance.LogInformation(
            $"{VhLogger.FormatType(this)} is listening on {VhLogger.Format(_localEndpointIpV4)}");
        _ = AcceptTcpClientLoop(_tcpListenerIpV4);

        // IpV6
        try {
            _tcpListenerIpV6 = new TcpListener(IPAddress.IPv6Any, 0);
            _tcpListenerIpV6.Start();
            _localEndpointIpV6 = (IPEndPoint)_tcpListenerIpV6.LocalEndpoint; //it is slow; make sure to cache it
            VhLogger.Instance.LogInformation(
                $"{VhLogger.FormatType(this)} is listening on {VhLogger.Format(_localEndpointIpV6)}");
            _ = AcceptTcpClientLoop(_tcpListenerIpV6);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex,
                $"Could not create listener on {VhLogger.Format(new IPEndPoint(IPAddress.IPv6Any, 0))}!");
        }
    }

    private async Task AcceptTcpClientLoop(TcpListener tcpListener)
    {
        var cancellationToken = _cancellationTokenSource.Token;
        var localEp = (IPEndPoint)tcpListener.LocalEndpoint;

        try {
            while (!cancellationToken.IsCancellationRequested) {
                var tcpClient = await tcpListener.AcceptTcpClientAsync().VhConfigureAwait();
                _ = ProcessClient(tcpClient, cancellationToken);
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
    public IPPacket[] ProcessOutgoingPacket(IList<IPPacket> ipPackets)
    {
        if (_localEndpointIpV4 == null)
            throw new InvalidOperationException(
                $"{nameof(_localEndpointIpV4)} has not been initialized! Did you call {nameof(Start)}!");

        _ipPackets.Clear(); // prevent reallocation in this intensive method
        var ret = _ipPackets;

        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var ipPacket = ipPackets[i];
            var catcherAddress = ipPacket.Version == IPVersion.IPv4 ? CatcherAddressIpV4 : CatcherAddressIpV6;
            var localEndPoint = ipPacket.Version == IPVersion.IPv4 ? _localEndpointIpV4 : _localEndpointIpV6;
            TcpPacket? tcpPacket = null;

            try {
                tcpPacket = PacketUtil.ExtractTcp(ipPacket);

                // check local endpoint
                if (localEndPoint == null)
                    throw new Exception("There is no localEndPoint registered for this packet.");

                // ignore new packets 
                if (_disposed)
                    throw new ObjectDisposedException(GetType().Name);

                // redirect to inbound
                if (Equals(ipPacket.DestinationAddress, catcherAddress)) {
                    var natItem = (NatItemEx?)vpnHoodClient.Nat.Resolve(ipPacket.Version, ipPacket.Protocol,
                                      tcpPacket.DestinationPort)
                                  ?? throw new Exception("Could not find incoming tcp destination in NAT.");

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
                        ? vpnHoodClient.Nat.Add(ipPacket, true)
                        : vpnHoodClient.Nat.Get(ipPacket) ??
                          throw new Exception("Could not find outgoing tcp destination in NAT.");

                    // set customData
                    if (syncCustomData != null)
                        natItem.CustomData = syncCustomData;

                    tcpPacket.SourcePort = natItem.NatId; // 1
                    ipPacket.DestinationAddress = ipPacket.SourceAddress; // 2
                    ipPacket.SourceAddress = catcherAddress; //3
                    tcpPacket.DestinationPort = (ushort)localEndPoint.Port; //4
                }

                PacketUtil.UpdateIpPacket(ipPacket);
                ret.Add(ipPacket);
            }
            catch (Exception ex) {
                if (tcpPacket != null) {
                    ret.Add(PacketUtil.CreateTcpResetReply(ipPacket, true));
                    PacketUtil.LogPacket(ipPacket,
                        "ClientHost: Error in processing packet. Dropping packet and sending TCP rest.",
                        LogLevel.Error, ex);
                }
                else {
                    PacketUtil.LogPacket(ipPacket, "ClientHost: Error in processing packet. Dropping packet.",
                        LogLevel.Error, ex);
                }
            }
        }

        return ret.ToArray(); //it is a shared buffer; to ToArray is necessary
    }

    public bool ShouldPassthru(IPPacket ipPacket, int sourcePort, int destinationPort)
    {
        return
            IsPassthruInProcessPacketsEnabled &&
            vpnHoodClient.SocketFactory.CanDetectInProcessPacket &&
            vpnHoodClient.SocketFactory.IsInProcessPacket(ipPacket.Protocol,
                new IPEndPoint(ipPacket.SourceAddress, sourcePort),
                new IPEndPoint(ipPacket.DestinationAddress, destinationPort));
    }

    private SyncCustomData? ProcessOutgoingSyncPacket(IPPacket ipPacket, TcpPacket tcpPacket)
    {
        var sync = tcpPacket is { Synchronize: true, Acknowledgment: false };
        if (!sync)
            return null;

        var syncCustomData = new SyncCustomData {
            Passthru = ShouldPassthru(ipPacket, tcpPacket.SourcePort, tcpPacket.DestinationPort),
            IsInIpRange = vpnHoodClient.IsInIpRange(ipPacket.DestinationAddress)
        };

        if (ipPacket.Version == IPVersion.IPv6)
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
        StreamProxyChannel? channel = null;
        var ipVersion = IPVersion.IPv4;

        try {
            // check cancellation
            Interlocked.Increment(ref _processingCount);
            cancellationToken.ThrowIfCancellationRequested();

            // config tcpOrgClient
            vpnHoodClient.SocketFactory.SetKeepAlive(orgTcpClient.Client, true);
            VhUtils.ConfigTcpClient(orgTcpClient, null, null);

            // get original remote from NAT
            var orgRemoteEndPoint = (IPEndPoint)orgTcpClient.Client.RemoteEndPoint;
            ipVersion = orgRemoteEndPoint.AddressFamily == AddressFamily.InterNetwork
                ? IPVersion.IPv4
                : IPVersion.IPv6;

            var natItem =
                (NatItemEx?)vpnHoodClient.Nat.Resolve(ipVersion, ProtocolType.Tcp, (ushort)orgRemoteEndPoint.Port) ??
                throw new Exception(
                    $"Could not resolve original remote from NAT! RemoteEndPoint: {VhLogger.Format(orgTcpClient.Client.RemoteEndPoint)}");

            var syncCustomData = natItem.CustomData as SyncCustomData?;

            // create a scope for the logger
            using var scope = VhLogger.Instance.BeginScope("LocalPort: {LocalPort}, RemoteEp: {RemoteEp}",
                natItem.SourcePort, VhLogger.Format(natItem.DestinationAddress) + ":" + natItem.DestinationPort);
            VhLogger.Instance.LogDebug(GeneralEventId.StreamProxyChannel, "New TcpProxy Request.");

            // check invalid income
            var catcherAddress = ipVersion == IPVersion.IPv4 ? CatcherAddressIpV4 : CatcherAddressIpV6;
            if (!Equals(orgRemoteEndPoint.Address, catcherAddress))
                throw new Exception("TcpProxy rejected an outbound connection!");

            // Filter by SNI
            var filterResult = await vpnHoodClient.DomainFilterService
                .Process(orgTcpClient.GetStream(), natItem.DestinationAddress, cancellationToken)
                .VhConfigureAwait();

            if (filterResult.Action == DomainFilterAction.Block) {
                VhLogger.Instance.LogInformation(GeneralEventId.Sni,
                    "Domain has been blocked. Domain: {Domain}",
                    VhLogger.FormatHostName(filterResult.DomainName));

                throw new Exception($"Domain has been blocked. Domain: {filterResult.DomainName}");
            }

            // Filter by IP
            var isInIpRange = syncCustomData?.IsInIpRange ?? vpnHoodClient.IsInIpRange(natItem.DestinationAddress);
            if (syncCustomData?.Passthru == true ||
                filterResult.Action == DomainFilterAction.Exclude ||
                (!isInIpRange && filterResult.Action != DomainFilterAction.Include)) {
                var channelId = Guid.NewGuid() + ":client";
                await vpnHoodClient.AddPassthruTcpStream(
                        new TcpClientStream(orgTcpClient, orgTcpClient.GetStream(), channelId),
                        new IPEndPoint(natItem.DestinationAddress, natItem.DestinationPort),
                        channelId, filterResult.ReadData, cancellationToken)
                    .VhConfigureAwait();

                _stat.TcpPassthruCount++;
                return;
            }

            // Create the Request
            var request = new StreamProxyChannelRequest {
                RequestId = Guid.NewGuid() + ":client",
                SessionId = vpnHoodClient.SessionId,
                SessionKey = vpnHoodClient.SessionKey,
                DestinationEndPoint = new IPEndPoint(natItem.DestinationAddress, natItem.DestinationPort),
                CipherKey = VhUtils.GenerateKey(),
                CipherLength = natItem.DestinationPort == 443 ? TunnelDefaults.TlsHandshakeLength : -1
            };

            // read the response
            requestResult = await vpnHoodClient.SendRequest<SessionResponse>(request, cancellationToken)
                .VhConfigureAwait();
            var proxyClientStream = requestResult.ClientStream;

            // create a StreamProxyChannel
            VhLogger.Instance.LogDebug(GeneralEventId.StreamProxyChannel,
                "Adding a channel to session. SessionId: {SessionId}...", VhLogger.FormatId(request.SessionId));
            var orgTcpClientStream =
                new TcpClientStream(orgTcpClient, orgTcpClient.GetStream(), request.RequestId + ":host");

            // flush initBuffer
            await proxyClientStream.Stream.WriteAsync(filterResult.ReadData, cancellationToken);

            // add stream proxy
            channel = new StreamProxyChannel(request.RequestId, orgTcpClientStream, proxyClientStream);
            vpnHoodClient.Tunnel.AddChannel(channel);
            _stat.TcpTunnelledCount++;
        }
        catch (Exception ex) {
            // disable IPv6 if detect the new network does not have IpV6
            if (ipVersion == IPVersion.IPv6 &&
                ex is SocketException { SocketErrorCode: SocketError.NetworkUnreachable })
                vpnHoodClient.IsIpV6SupportedByClient = false;

            if (channel != null) await channel.DisposeAsync().VhConfigureAwait();
            if (requestResult != null) await requestResult.DisposeAsync().VhConfigureAwait();
            orgTcpClient.Dispose();
            VhLogger.LogError(GeneralEventId.StreamProxyChannel, ex, "");
        }
        finally {
            Interlocked.Decrement(ref _processingCount);
        }
    }


    public ValueTask DisposeAsync()
    {
        if (_disposed) return default;
        _disposed = true;
        _cancellationTokenSource.Cancel();
        _tcpListenerIpV4?.Stop();
        _tcpListenerIpV6?.Stop();

        return default;
    }

    private class ClientHostStat : IClientHostStat
    {
        public int TcpTunnelledCount { get; set; }
        public int TcpPassthruCount { get; set; }
    }

    public struct SyncCustomData
    {
        public required bool? Passthru { get; init; }
        public required bool IsInIpRange { get; init; }
    }
}