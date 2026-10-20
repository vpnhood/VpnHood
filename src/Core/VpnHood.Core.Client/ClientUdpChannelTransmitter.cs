using System.Net;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Client;

internal class ClientUdpChannelTransmitter : UdpChannelTransmitter
{
    public SessionUdpTransport UdpTransport { get; }

    public ClientUdpChannelTransmitter(ISocketFactory socketFactory, ulong sessionId, ReadOnlySpan<byte> sessionKey,
        IPEndPoint remoteEndPoint, TransferBufferSize? bufferSize, ulong firstSendSequenceNumber)
        : base(socketFactory.CreateUdpClient(remoteEndPoint.AddressFamily))
    {
        UdpTransport = new SessionUdpTransport(
            this,
            sessionId: sessionId, sessionKey,
            remoteEndPoint: remoteEndPoint,
            isServer: false,
            firstSendSequenceNumber: firstSendSequenceNumber);

        BufferSize = bufferSize;
    }

    protected override SessionUdpTransport SessionIdToUdpTransport(ulong sessionId)
    {
        return UdpTransport;
    }

    public override void Dispose()
    {
        // the base first: a decrypt its teardown cuts off then meets a disposed transmitter, which the read loop
        // takes as its own end
        base.Dispose();
        UdpTransport.Dispose();
    }
}
