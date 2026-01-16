using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Channels;

public class UdpChannel : PacketChannel
{
    private readonly Memory<byte> _buffer = new byte[TunnelDefaults.MaxPacketSize];
    private readonly TaskCompletionSource<bool> _readingTask = new();
    private readonly bool _leaveUdpTransportOpen;

    public IUdpTransport UdpTransport { get; }
    public override int OverheadLength => UdpTransport.OverheadLength;

    public UdpChannel(IUdpTransport udpTransport, UdpChannelOptions options) : base(options)
    {
        UdpTransport = udpTransport;
        UdpTransport.DataReceived = OnDataReceived;
        _leaveUdpTransportOpen = options.LeaveUdpTransportOpen;
    }

    protected override Task StartReadTask()
    {
        // already started by UdpChannelTransmitter
        return _readingTask.Task;
    }

    public async Task SendBuffer(Memory<byte> buffer)
    {
        await UdpTransport.SendAsync(buffer).Vhc();
    }

    protected override async ValueTask SendPacketsAsync(IList<IpPacket> ipPackets)
    {
        try {
            // copy packets to buffer (payload only, transmitter adds its own overhead)
            var bufferIndex = 0;

            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < ipPackets.Count; i++) {
                var ipPacket = ipPackets[i];
                var packetBytes = ipPacket.Buffer;

                // flush buffer if this packet does not fit
                if (bufferIndex > 0 &&
                    bufferIndex + packetBytes.Length > _buffer.Length - UdpTransport.OverheadLength) {
                    await SendBuffer(_buffer[..bufferIndex]).Vhc();
                    bufferIndex = 0;
                }

                // check if packet is too big
                if (bufferIndex + packetBytes.Length > _buffer.Length - UdpTransport.OverheadLength) {
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
            if (bufferIndex > 0)
                await SendBuffer(_buffer[..bufferIndex]).Vhc();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) {
            // ignore cancellation
            Dispose();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.Udp, ex,
                "Unexpected error in sending packets. ChannelId: {ChannelId}", ChannelId);

            if (!CanRetry(ex))
                Dispose();
        }
    }

    private static bool CanRetry(Exception ex)
    {
        if (ex is not SocketException socketException)
            return false; // not a socket exception, no retry

        return socketException.SocketErrorCode switch {
            SocketError.TimedOut => true,
            SocketError.Interrupted => true,
            SocketError.NetworkUnreachable => true,
            SocketError.HostUnreachable => true,
            SocketError.ConnectionReset => true, // Common in UDP when destination port is closed
            SocketError.TryAgain => true, // Possibly can retry depending on your use case
            _ => false
        };
    }

    private static IpPacket ReadNextPacketKeepMemory(Memory<byte> buffer)
    {
        var packetLength = PacketUtil.ReadPacketLength(buffer.Span);
        var packet = PacketBuilder.Attach(buffer[..packetLength]);
        return packet;
    }

    public void OnDataReceived(Memory<byte> buffer)
    {
        // read all packets
        var bufferIndex = 0;
        while (bufferIndex < buffer.Length) {
            var ipPacket = ReadNextPacketKeepMemory(buffer[bufferIndex..]);
            bufferIndex += ipPacket.PacketLength;
            OnPacketReceived(ipPacket);
        }
    }

    protected override void DisposeManaged()
    {
        if (!_leaveUdpTransportOpen)
            UdpTransport.Dispose();

        // finalize reading task
        _readingTask.TrySetResult(true);

        base.DisposeManaged();
    }
}