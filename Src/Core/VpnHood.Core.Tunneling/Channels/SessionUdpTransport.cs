using System.Net;
using VpnHood.Core.Tunneling.Cryptography;

namespace VpnHood.Core.Tunneling.Channels;

public class SessionUdpTransport(
    UdpChannelTransmitter channelTransmitter,
    ulong sessionId,
    ReadOnlySpan<byte> key,
    IPEndPoint? remoteEndPoint,
    bool isServer)
    : IUdpTransport
{
    public bool IsServer { get; } = isServer;
    public IPEndPoint? RemoteEndPoint { get; set; } = remoteEndPoint;
    private ICryptor SendCryptor { get; } = new AesGcmCryptor(key, UdpChannelTransmitter.TagLength);
    internal ICryptor ReceiveCryptor { get; } = new AesGcmCryptor(key, UdpChannelTransmitter.TagLength);
    public UdpChannelTransmitter ChannelTransmitter { get; set; } = channelTransmitter;
    public Action<Memory<byte>>? DataReceived { get; set; }
    public int OverheadLength => TunnelDefaults.MtuOverhead;
    public bool Connected => ChannelTransmitter.Connected;

    public Task SendAsync(Memory<byte> buffer)
    {
        return RemoteEndPoint is not null
            ? ChannelTransmitter.SendAsync(sessionId, buffer, RemoteEndPoint, SendCryptor)
            : throw new InvalidOperationException("RemoteEndPoint is not set.");
    }

    public void Dispose()
    {
        SendCryptor.Dispose();
        ReceiveCryptor.Dispose();
    }
}
