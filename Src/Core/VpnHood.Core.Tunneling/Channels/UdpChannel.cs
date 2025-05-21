using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Channels;

public class UdpChannel(UdpChannelOptions options)
    : PacketTransport(new PacketTransportOptions {
        AutoDisposePackets = options.AutoDisposePackets,
        Blocking = options.Blocking,
    }), IPacketChannel
{
    private IPEndPoint? _lastRemoteEp;
    private readonly Memory<byte> _buffer = new byte[TunnelDefaults.Mtu + UdpChannelTransmitter.HeaderLength];
    private UdpChannelTransmitter? _udpChannelTransmitter;
    private readonly BufferCryptor _sessionCryptorWriter = new(options.SessionKey);
    private readonly BufferCryptor _sessionCryptorReader = new(options.SessionKey);
    private readonly long _cryptorPosBase = options.LeaveTransmitterOpen ? DateTime.UtcNow.Ticks : 0; // make sure server does not use client position as IV
    private bool _disposed;
    private readonly ulong _sessionId = options.SessionId;
    private readonly int _protocolVersion = options.ProtocolVersion;
    private readonly bool _leaveTransmitterOpen = options.LeaveTransmitterOpen;

    public string ChannelId { get; } = Guid.NewGuid().ToString();
    public bool IsStream => false;
    public bool IsClosePending => false;
    public bool Connected { get; private set; }
    public DateTime LastActivityTime => PacketStat.LastActivityTime;
    public Traffic Traffic => new (PacketStat.SentBytes, PacketStat.ReceivedBytes);

    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (Connected)
            throw new InvalidOperationException("The udpChannel is already started.");

        Connected = true;
    }

    public async Task SendBuffer(Memory<byte> buffer)
    {
        if (_lastRemoteEp == null)
            throw new InvalidOperationException("RemoveEndPoint has not been initialized yet in UdpChannel.");

        if (_udpChannelTransmitter == null)
            throw new InvalidOperationException("UdpChannelTransmitter has not been initialized yet in UdpChannel.");

        // encrypt packets
        var sessionCryptoPosition = _cryptorPosBase + PacketStat.SentBytes;
        _sessionCryptorWriter.Cipher(buffer.Span[UdpChannelTransmitter.HeaderLength..],
            sessionCryptoPosition);

        // send buffer
        await _udpChannelTransmitter
            .SendAsync(_lastRemoteEp, _sessionId, sessionCryptoPosition, buffer, _protocolVersion)
            .VhConfigureAwait();
    }

    protected override async ValueTask SendPacketsAsync(IList<IpPacket> ipPackets)
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        if (!Connected)
            throw new Exception($"The UdpChannel is disconnected. ChannelId: {ChannelId}.");

        try {
            // copy packets to buffer
            var bufferIndex = UdpChannelTransmitter.HeaderLength;

            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < ipPackets.Count; i++) {
                var ipPacket = ipPackets[i];
                var packetBytes = ipPacket.Buffer;

                // flush buffer if this packet does not fit
                if (bufferIndex > UdpChannelTransmitter.HeaderLength && bufferIndex + packetBytes.Length > _buffer.Length) {
                    await SendBuffer(_buffer[..bufferIndex]).VhConfigureAwait();
                    bufferIndex = UdpChannelTransmitter.HeaderLength;
                }

                // check if packet is too big
                if (bufferIndex + packetBytes.Length > _buffer.Length) {
                    VhLogger.Instance.LogWarning(GeneralEventId.Udp,
                        "Packet is too big to send. PacketLength: {PacketLength}",
                        packetBytes.Length);
                    continue;
                }

                // add packet to buffer
                packetBytes.Span.CopyTo(_buffer.Span[bufferIndex..]);
                bufferIndex += packetBytes.Length;
            }

            // send remaining buffer
            if (bufferIndex > UdpChannelTransmitter.HeaderLength) {
                await SendBuffer(_buffer[..bufferIndex]).VhConfigureAwait();
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.Udp, ex,
                "Error in sending packets. ChannelId: {ChannelId}", ChannelId);

            if (IsInvalidState(ex))
                await DisposeAsync().VhConfigureAwait();
        }
    }

    public void SetRemote(UdpChannelTransmitter udpChannelTransmitter, IPEndPoint remoteEndPoint)
    {
        _udpChannelTransmitter = udpChannelTransmitter;
        _lastRemoteEp = remoteEndPoint;
    }

    private static IpPacket ReadNextPacketKeepMemory(Memory<byte> buffer)
    {
        var packetLength = PacketUtil.ReadPacketLength(buffer.Span);
        var packet = PacketBuilder.Attach(buffer[..packetLength]);
        return packet;
    }

    public void OnReceiveData(Memory<byte> buffer, long cryptorPosition)
    {
        _sessionCryptorReader.Cipher(buffer.Span, cryptorPosition);

        // read all packets
        var bufferIndex = 0;
        while (bufferIndex < buffer.Length) {
            var ipPacket = ReadNextPacketKeepMemory(buffer[bufferIndex..]);
            bufferIndex += ipPacket.PacketLength;
            OnPacketReceived(ipPacket);
        }
    }

    private bool IsInvalidState(Exception ex)
    {
        return _disposed || ex is ObjectDisposedException or SocketException {
            SocketErrorCode: SocketError.InvalidArgument
        };
    }

    public ValueTask DisposeAsync(bool graceful)
    {
        _ = graceful;
        return DisposeAsync();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return default;
        _disposed = true;
        Connected = false;
        if (!_leaveTransmitterOpen)
            _udpChannelTransmitter?.Dispose();

        return default;
    }
}