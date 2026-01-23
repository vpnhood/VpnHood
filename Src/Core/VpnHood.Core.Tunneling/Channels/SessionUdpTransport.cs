using System.Net;

namespace VpnHood.Core.Tunneling.Channels;

public class SessionUdpTransport(
    UdpChannelTransmitter channelTransmitter,
    ulong sessionId,
    Span<byte> key,
    IPEndPoint? remoteEndPoint,
    bool isServer)
    : IUdpTransport
{
    public bool IsServer { get; } = isServer;
    public IPEndPoint? RemoteEndPoint { get; set; } = remoteEndPoint;
    internal AesCtrCryptor Cryptor { get; } = new(key);
    public UdpChannelTransmitter ChannelTransmitter { get; set; } = channelTransmitter;
    public Action<Memory<byte>>? DataReceived { get; set; }
    public int OverheadLength => UdpChannelTransmitter.HeaderLength;
    public bool Connected => ChannelTransmitter.Connected;

    public Task SendAsync(Memory<byte> buffer)
    {
        return RemoteEndPoint is not null
            ? ChannelTransmitter.SendAsync(sessionId, buffer, RemoteEndPoint, Cryptor)
            : throw new InvalidOperationException("RemoteEndPoint is not set.");
    }

    public void Dispose()
    {
        Cryptor.Dispose();
    }
}
