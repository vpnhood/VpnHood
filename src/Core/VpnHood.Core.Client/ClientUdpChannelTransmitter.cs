using System.Net;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Client;

internal class ClientUdpChannelTransmitter : UdpChannelTransmitter
{
    public IUdpTransport UdpTransport { get; }

    public ClientUdpChannelTransmitter(ISocketFactory socketFactory, ulong sessionId, ReadOnlySpan<byte> sessionKey,
        IPEndPoint remoteEndPoint, TransferBufferSize? bufferSize)
        : base(socketFactory.CreateUdpClient(remoteEndPoint.AddressFamily))
    {
        UdpTransport = new SessionUdpTransport(
            this,
            sessionId: sessionId, sessionKey,
            remoteEndPoint: remoteEndPoint,
            isServer: false);

        BufferSize = bufferSize;
    }

    protected override SessionUdpTransport SessionIdToUdpTransport(ulong sessionId)
    {
        return (SessionUdpTransport)UdpTransport;
    }

    public override void Dispose()
    {
        UdpTransport.Dispose();
        base.Dispose();
    }
}