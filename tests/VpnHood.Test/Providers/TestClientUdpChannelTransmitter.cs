using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Test.Providers;

// a client's side of a session on a socket of its own: one transport, to the one server endpoint
internal class TestClientUdpChannelTransmitter : UdpChannelTransmitter
{
    public SessionUdpTransport UdpTransport { get; }

    public TestClientUdpChannelTransmitter(IPEndPoint remoteEndPoint, ulong sessionId, byte[] sessionKey,
        ulong firstSendSequenceNumber)
        : base(new UdpClient(0, remoteEndPoint.AddressFamily))
    {
        UdpTransport = new SessionUdpTransport(this, sessionId, sessionKey, remoteEndPoint, false,
            firstSendSequenceNumber);
    }

    protected override SessionUdpTransport SessionIdToUdpTransport(ulong sessionId)
    {
        _ = sessionId;
        return UdpTransport;
    }

    public override void Dispose()
    {
        base.Dispose();
        UdpTransport.Dispose();
    }
}
