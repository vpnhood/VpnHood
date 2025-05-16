using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Channels;

public class UdpChannel(ulong sessionId, byte[] sessionKey, bool isServer, int protocolVersion)
    : IDatagramChannel
{
    private IPEndPoint? _lastRemoteEp;
    private readonly byte[] _buffer = new byte[TunnelDefaults.Mtu + UdpChannelTransmitter.HeaderLength];
    private UdpChannelTransmitter? _udpChannelTransmitter;
    private readonly BufferCryptor _sessionCryptorWriter = new(sessionKey);
    private readonly BufferCryptor _sessionCryptorReader = new(sessionKey);
    private PacketReceivedEventArgs? _packetReceivedEventArgs;
    private readonly IpPacket[] _sendingPackets = [null!];
    private readonly long _cryptorPosBase = isServer ? DateTime.UtcNow.Ticks : 0; // make sure server does not use client position as IV
    private bool _disposed;

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public string ChannelId { get; } = Guid.NewGuid().ToString();
    public bool IsStream => false;
    public bool IsClosePending => false;
    public bool Connected { get; private set; }
    public DateTime LastActivityTime { get; private set; }
    public Traffic Traffic { get; } = new();

    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (Connected)
            throw new InvalidOperationException("The udpChannel is already started.");

        Connected = true;
        LastActivityTime = FastDateTime.Now;
    }

    // it is not thread safe
    public Task SendPacketAsync(IpPacket packet)
    {
        _sendingPackets[0] = packet;
        return SendPacketAsync(_sendingPackets);
    }

    public async Task SendBuffer(byte[] buffer, int bufferLength)
    {
        if (_lastRemoteEp == null)
            throw new InvalidOperationException("RemoveEndPoint has not been initialized yet in UdpChannel.");

        if (_udpChannelTransmitter == null)
            throw new InvalidOperationException("UdpChannelTransmitter has not been initialized yet in UdpChannel.");

        // encrypt packets
        var sessionCryptoPosition = _cryptorPosBase + Traffic.Sent;
        _sessionCryptorWriter.Cipher(buffer,
            UdpChannelTransmitter.HeaderLength,
            bufferLength - UdpChannelTransmitter.HeaderLength, 
            sessionCryptoPosition);

        // send buffer
        var ret = await _udpChannelTransmitter
            .SendAsync(_lastRemoteEp, sessionId, sessionCryptoPosition, buffer, bufferLength, protocolVersion)
            .VhConfigureAwait();

        Traffic.Sent += ret;
        LastActivityTime = FastDateTime.Now;
    }

    public async Task SendPacketAsync(IList<IpPacket> ipPackets)
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
                    await SendBuffer(_buffer, bufferIndex).VhConfigureAwait();
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
                packetBytes.Span.CopyTo(_buffer.AsSpan(bufferIndex));
                bufferIndex += packetBytes.Length;
            }

            // send remaining buffer
            if (bufferIndex > UdpChannelTransmitter.HeaderLength) {
                await SendBuffer(_buffer, bufferIndex).VhConfigureAwait();
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

    public void OnReceiveData(long cryptorPosition, byte[] buffer, int bufferIndex)
    {
        _sessionCryptorReader.Cipher(buffer, bufferIndex, buffer.Length - bufferIndex, cryptorPosition);

        // read all packets
        try {
            _packetReceivedEventArgs ??= new PacketReceivedEventArgs([]);
            _packetReceivedEventArgs.IpPackets.Clear();

            while (bufferIndex < buffer.Length) {
                var ipPacket = PacketUtil.ReadNextPacket(buffer, ref bufferIndex);
                Traffic.Received += ipPacket.PacketLength;
                _packetReceivedEventArgs.IpPackets.Add(ipPacket);
            }

            PacketReceived?.Invoke(this, _packetReceivedEventArgs);
            LastActivityTime = FastDateTime.Now;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(GeneralEventId.Udp, ex, "Error in processing packets.");
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
        if (!isServer)
            _udpChannelTransmitter?.Dispose();

        return default;
    }
}