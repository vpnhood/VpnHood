using System.Net;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Core.Client;

// The client's side of its session's UDP. It sends to the server's UDP endpoint through a socket of its own, which its
// session opens when it turns to UDP and again once a network change or a failure has closed it. Its route never
// follows a datagram, as the server's does: a copy sent from elsewhere moves nothing
internal sealed class ClientUdpPacketChannel(
    ISocketFactory socketFactory,
    IPEndPoint serverEndPoint,
    TransferBufferSize? bufferSize,
    ulong sessionId,
    ReadOnlySpan<byte> sessionKey,
    ulong firstSendSequenceNumber,
    TrafficMeter trafficMeter)
    : UdpPacketChannel(sessionId, sessionKey, isServer: false, firstSendSequenceNumber, trafficMeter),
        IUdpDatagramHandler
{
    // an old socket's read loop may still hand a datagram over while the new one's does
    private readonly Lock _receiveLock = new();

    // a socket is made and closed under it, so none is made on a disposed channel
    private readonly Lock _socketLock = new();
    private volatile UdpRoute? _route;

    public bool IsSocketOpen => _route is { Transmitter.Connected: true };

    // a new socket, unless one is open
    public void OpenSocket()
    {
        lock (_socketLock) {
            ObjectDisposedException.ThrowIf(IsDisposing, this);
            if (!IsSocketOpen)
                _route = new UdpRoute(CreateTransmitter(), serverEndPoint);
        }
    }

    // a network change: the session opens a new one, from the new network
    public void CloseSocket()
    {
        lock (_socketLock)
            _route?.Transmitter.Dispose();
    }

    public void OnDatagramReceived(UdpChannelTransmitter transmitter, Memory<byte> datagram, IPEndPoint remoteEndPoint)
    {
        lock (_receiveLock)
            Open(datagram.Span);

        HandUp(datagram[UdpDatagram.HeaderLength..]);
    }

    protected override UdpRoute? GetRoute() => _route;

    private UdpChannelTransmitter CreateTransmitter()
    {
        var udpClient = socketFactory.CreateUdpClient(serverEndPoint.AddressFamily);
        try {
            return new UdpChannelTransmitter(udpClient, this) { BufferSize = bufferSize };
        }
        catch {
            udpClient.Dispose();
            throw;
        }
    }

    // the socket first: a datagram the teardown cuts off then meets a closed socket, which its read loop takes as its
    // end
    protected override void DisposeManaged()
    {
        lock (_socketLock)
            _route?.Transmitter.Dispose();

        base.DisposeManaged();
    }
}
