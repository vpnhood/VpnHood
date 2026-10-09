using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Core.Tunneling.Connections;

namespace VpnHood.Core.Tunneling.Channels;

public class StreamPacketChannel(StreamPacketChannelOptions options)
    : PacketChannel(options)
{
    private readonly int _receiveBufferSize = options.BufferSize.Receive;
    private readonly Memory<byte> _sendBuffer = new byte[options.BufferSize.Send];
    private readonly IStreamConnection _streamConnection = options.StreamConnection;

    public DateTime RequestTime { get; } = options.RequestTime;
    public override int OverheadLength => 0;

    protected override async ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var cancellationToken = CancellationToken;

        // copy packets to buffer
        var buffer = _sendBuffer;
        var bufferIndex = 0;

        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var ipPacket = ipPackets[i];
            var packetBytes = ipPacket.Buffer;

            // flush current buffer if this packet does not fit
            if (bufferIndex > 0 && bufferIndex + packetBytes.Length > buffer.Length) {
                await WriteBuffer(buffer[..bufferIndex], cancellationToken);
                bufferIndex = 0;
            }

            // Write the packet directly if it does not fit in the buffer or there is only one packet
            if (packetBytes.Length > buffer.Length || ipPackets.Count == 1) {
                await WriteBuffer(packetBytes, cancellationToken);
                continue;
            }

            packetBytes.Span.CopyTo(buffer.Span[bufferIndex..]);
            bufferIndex += packetBytes.Length;
        }

        // send remaining buffer
        if (bufferIndex > 0)
            await WriteBuffer(buffer[..bufferIndex], cancellationToken);
    }

    // the send limit is not waited on here: a throttled tunnel paces its packets before they reach a channel
    private async ValueTask WriteBuffer(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        await _streamConnection.Stream.WriteAsync(buffer, cancellationToken).Vhc();
        TrafficMeter?.OnSent(buffer.Length);
    }

    protected override async Task StartReadTask()
    {
        var cancellationToken = CancellationToken;

        using var streamPacketReader =
            new StreamPacketReader(_streamConnection.Stream, _receiveBufferSize);

        // stop reading if State is not Connected (Such as getting the close request)
        while (!cancellationToken.IsCancellationRequested) {
            var ipPacket = await streamPacketReader.ReadAsync(cancellationToken).Vhc();
            if (ipPacket == null) {
                VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel, "Packet stream ended. Terminating read task.");
                break;
            }

            // the receive limit, enforced by reading slower; a packet whose wait is cut is not delivered
            TrafficMeter?.OnReceived(ipPacket.PacketLength);
            if (TrafficMeter != null) {
                var throttleTask = TrafficMeter.ThrottleReceiveAsync(ipPacket.PacketLength, cancellationToken);
                if (!throttleTask.IsCompletedSuccessfully) {
                    try {
                        await throttleTask.Vhc();
                    }
                    catch {
                        ipPacket.Dispose();
                        throw;
                    }
                }
            }

            OnPacketReceived(ipPacket);
        }
    }

    protected override void DisposeManaged()
    {
        Stop();
        _streamConnection.Dispose();

        base.DisposeManaged();
    }
}