using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using PacketDotNet.Ieee80211;
using VpnHood.Client.ConnectorServices;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Channels;
using VpnHood.Tunneling.ClientStreams;
using VpnHood.Tunneling.Messaging;
using ProtocolType = PacketDotNet.ProtocolType;

namespace VpnHood.Client;

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

    public IPAddress CatcherAddressIpV4 { get; } = catcherAddressIpV4;
    public IPAddress CatcherAddressIpV6 { get; } = catcherAddressIpV6;


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
        try
        {
            _tcpListenerIpV6 = new TcpListener(IPAddress.IPv6Any, 0);
            _tcpListenerIpV6.Start();
            _localEndpointIpV6 = (IPEndPoint)_tcpListenerIpV6.LocalEndpoint; //it is slow; make sure to cache it
            VhLogger.Instance.LogInformation(
                $"{VhLogger.FormatType(this)} is listening on {VhLogger.Format(_localEndpointIpV6)}");
            _ = AcceptTcpClientLoop(_tcpListenerIpV6);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex,
                $"Could not create listener on {VhLogger.Format(new IPEndPoint(IPAddress.IPv6Any, 0))}!");
        }
    }

    private async Task AcceptTcpClientLoop(TcpListener tcpListener)
    {
        var cancellationToken = _cancellationTokenSource.Token;
        var localEp = (IPEndPoint)tcpListener.LocalEndpoint;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var tcpClient = await tcpListener.AcceptTcpClientAsync().VhConfigureAwait();
                _ = ProcessClient(tcpClient, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (!_disposed)
                VhLogger.LogError(GeneralEventId.Tcp, ex, "");
        }
        finally
        {
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
        for (var i = 0; i < ipPackets.Count; i++)
        {
            var ipPacket = ipPackets[i];
            var loopbackAddress = ipPacket.Version == IPVersion.IPv4 ? CatcherAddressIpV4 : CatcherAddressIpV6;
            var localEndPoint = ipPacket.Version == IPVersion.IPv4 ? _localEndpointIpV4 : _localEndpointIpV6;
            TcpPacket? tcpPacket = null;

            try
            {
                tcpPacket = PacketUtil.ExtractTcp(ipPacket);
                GetSniFromPackets(tcpPacket);

                // check local endpoint
                if (localEndPoint == null)
                    throw new Exception("There is no localEndPoint registered for this packet.");

                // ignore new packets 
                if (_disposed)
                    throw new ObjectDisposedException(GetType().Name);

                // redirect to inbound
                if (Equals(ipPacket.DestinationAddress, loopbackAddress))
                {
                    var natItem = (NatItemEx?)vpnHoodClient.Nat.Resolve(ipPacket.Version, ipPacket.Protocol,
                                      tcpPacket.DestinationPort)
                                  ?? throw new Exception("Could not find incoming tcp destination in NAT.");

                    ipPacket.SourceAddress = natItem.DestinationAddress;
                    ipPacket.DestinationAddress = natItem.SourceAddress;
                    tcpPacket.SourcePort = natItem.DestinationPort;
                    tcpPacket.DestinationPort = natItem.SourcePort;
                }

                // Redirect outbound to the local address
                else
                {
                    var sync = tcpPacket is { Synchronize: true, Acknowledgment: false };
                    var natItem = sync
                        ? vpnHoodClient.Nat.Add(ipPacket, true)
                        : vpnHoodClient.Nat.Get(ipPacket) ??
                          throw new Exception("Could not find outgoing tcp destination in NAT.");

                    tcpPacket.SourcePort = natItem.NatId; // 1
                    ipPacket.DestinationAddress = ipPacket.SourceAddress; // 2
                    ipPacket.SourceAddress = loopbackAddress; //3
                    tcpPacket.DestinationPort = (ushort)localEndPoint.Port; //4
                }

                PacketUtil.UpdateIpPacket(ipPacket);
                ret.Add(ipPacket);
            }
            catch (Exception ex)
            {
                if (tcpPacket != null)
                {
                    ret.Add(PacketUtil.CreateTcpResetReply(ipPacket, true));
                    PacketUtil.LogPacket(ipPacket,
                        "ClientHost: Error in processing packet. Dropping packet and sending TCP rest.", LogLevel.Error,
                        ex);
                }
                else
                {
                    PacketUtil.LogPacket(ipPacket, "ClientHost: Error in processing packet. Dropping packet.",
                        LogLevel.Error, ex);
                }
            }
        }

        return ret.ToArray(); //it is a shared buffer; to ToArray is necessary
    }

    private async Task ProcessClient(TcpClient orgTcpClient, CancellationToken cancellationToken)
    {
        if (orgTcpClient is null) throw new ArgumentNullException(nameof(orgTcpClient));
        ConnectorRequestResult<SessionResponse>? requestResult = null;
        StreamProxyChannel? channel = null;

        try
        {
            // check cancellation
            Interlocked.Increment(ref _processingCount);
            cancellationToken.ThrowIfCancellationRequested();

            // config tcpOrgClient
            vpnHoodClient.SocketFactory.SetKeepAlive(orgTcpClient.Client, true);
            VhUtil.ConfigTcpClient(orgTcpClient, null, null);

            // get original remote from NAT
            var orgRemoteEndPoint = (IPEndPoint)orgTcpClient.Client.RemoteEndPoint;
            var ipVersion = orgRemoteEndPoint.AddressFamily == AddressFamily.InterNetwork
                ? IPVersion.IPv4
                : IPVersion.IPv6;

            var natItem =
                (NatItemEx?)vpnHoodClient.Nat.Resolve(ipVersion, ProtocolType.Tcp, (ushort)orgRemoteEndPoint.Port)
                ?? throw new Exception(
                    $"Could not resolve original remote from NAT! RemoteEndPoint: {VhLogger.Format(orgTcpClient.Client.RemoteEndPoint)}");

            // create a scope for the logger
            using var scope = VhLogger.Instance.BeginScope("LocalPort: {LocalPort}, RemoteEp: {RemoteEp}",
                natItem.SourcePort, VhLogger.Format(natItem.DestinationAddress) + ":" + natItem.DestinationPort);
            VhLogger.Instance.LogTrace(GeneralEventId.StreamProxyChannel, "New TcpProxy Request.");

            // check invalid income
            var loopbackAddress = ipVersion == IPVersion.IPv4 ? CatcherAddressIpV4 : CatcherAddressIpV6;
            if (!Equals(orgRemoteEndPoint.Address, loopbackAddress))
                throw new Exception("TcpProxy rejected an outbound connection!");

            // Check IpFilter
            if (!vpnHoodClient.IsInIpRange(natItem.DestinationAddress))
            {
                var channelId = Guid.NewGuid() + ":client";
                await vpnHoodClient.AddPassthruTcpStream(
                        new TcpClientStream(orgTcpClient, orgTcpClient.GetStream(), channelId),
                        new IPEndPoint(natItem.DestinationAddress, natItem.DestinationPort),
                        channelId, cancellationToken)
                    .VhConfigureAwait();
                return;
            }

            // Create the Request
            var request = new StreamProxyChannelRequest
            {
                RequestId = Guid.NewGuid() + ":client",
                SessionId = vpnHoodClient.SessionId,
                SessionKey = vpnHoodClient.SessionKey,
                DestinationEndPoint = new IPEndPoint(natItem.DestinationAddress, natItem.DestinationPort),
                CipherKey = VhUtil.GenerateKey(),
                CipherLength = natItem.DestinationPort == 443 ? TunnelDefaults.TlsHandshakeLength : -1
            };

            // read the response
            requestResult = await vpnHoodClient.SendRequest<SessionResponse>(request, cancellationToken)
                .VhConfigureAwait();
            var proxyClientStream = requestResult.ClientStream;

            // create a StreamProxyChannel
            VhLogger.Instance.LogTrace(GeneralEventId.StreamProxyChannel,
                $"Adding a channel to session {VhLogger.FormatId(request.SessionId)}...");
            var orgTcpClientStream =
                new TcpClientStream(orgTcpClient, orgTcpClient.GetStream(), request.RequestId + ":host");

            channel = new StreamProxyChannel(request.RequestId, orgTcpClientStream, proxyClientStream);
            vpnHoodClient.Tunnel.AddChannel(channel);
        }
        catch (Exception ex)
        {
            if (channel != null) await channel.DisposeAsync().VhConfigureAwait();
            if (requestResult != null) await requestResult.DisposeAsync().VhConfigureAwait();
            orgTcpClient.Dispose();
            VhLogger.LogError(GeneralEventId.StreamProxyChannel, ex, "");
        }
        finally
        {
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

    public static string? GetSniFromPackets(TcpPacket tcpPacket)
    {
        var payloadData = tcpPacket.PayloadData;
        if (payloadData == null || payloadData.Length == 0) 
            return null;

        if (payloadData[0] != 0x16 || payloadData[5] != 0x01)
            return null;

        int sessionIdLength = payloadData[43];
        var cipherSuitesLength = (payloadData[44 + sessionIdLength] << 8) | payloadData[44 + sessionIdLength + 1];
        int compressionMethodsLength = payloadData[44 + sessionIdLength + 2 + cipherSuitesLength];
        var extensionsStart = 44 + sessionIdLength + 2 + cipherSuitesLength + 1 + compressionMethodsLength;
        if (extensionsStart + 2 >= payloadData.Length) 
            return null;

        var extensionsLength = (payloadData[extensionsStart] << 8) | payloadData[extensionsStart + 1];
        var currentPos = extensionsStart + 2;
        if (currentPos + extensionsLength > payloadData.Length) 
            return null;

        while (currentPos < extensionsStart + 2 + extensionsLength)
        {
            if (currentPos + 4 > payloadData.Length) 
                return null;

            var extensionType = (payloadData[currentPos] << 8) | payloadData[currentPos + 1];
            var extensionLength = (payloadData[currentPos + 2] << 8) | payloadData[currentPos + 3];
            currentPos += 4;

            if (currentPos + extensionLength > payloadData.Length) 
                return null;

            if (extensionType == 0x0000)
            {
                if (currentPos + 2 > payloadData.Length) 
                    return null;

                var serverNameListLength = (payloadData[currentPos] << 8) | payloadData[currentPos + 1];
                currentPos += 2;

                if (currentPos + serverNameListLength > payloadData.Length) 
                    return null;

                // int serverNameType = payloadData[currentPos];
                var serverNameLength = (payloadData[currentPos + 1] << 8) | payloadData[currentPos + 2];
                currentPos += 3;

                if (currentPos + serverNameLength > payloadData.Length) 
                    return null;

                var sni = Encoding.ASCII.GetString(payloadData, currentPos, serverNameLength);
                Console.WriteLine(sni);
                return sni;
            }

            currentPos += extensionLength;
        }

        return null;
    }

}
