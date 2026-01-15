using System.Net;
using System.Security.Cryptography;

namespace VpnHood.Core.Tunneling.Channels;

public class SessionUdpTransport(
    UdpChannelTransmitter2 transmitter, ulong sessionId, Span<byte> key, IPEndPoint? remoteEndPoint, bool isServer)
    : IUdpTransport
{
    public bool IsServer { get; } = isServer;
    public IPEndPoint? RemoteEndPoint { get; set; } = remoteEndPoint;
    internal AesGcm AesGcm { get; } = new(key, 16);
    public UdpChannelTransmitter2 Transmitter { get; set; } = transmitter;
    public Action<Memory<byte>>? DataReceived { get; set; }
    public int OverheadLength => UdpChannelTransmitter2.HeaderLength;

    public Task SendAsync(Memory<byte> buffer)
    {
        return RemoteEndPoint is not null 
            ? Transmitter.SendAsync(sessionId, buffer, RemoteEndPoint, AesGcm) 
            : throw new InvalidOperationException("RemoteEndPoint is not set.");
    }

    public void Dispose()
    {
        AesGcm.Dispose();
    }
}