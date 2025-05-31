using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.ClientStreams;

namespace VpnHood.Core.Tunneling.Channels;

public class StreamPacketChannel(StreamPacketChannelOptions options) : PacketChannel(options)
{
    private readonly Memory<byte> _buffer = new byte[0xFFFF * 4];
    private readonly IClientStream _clientStream = options.ClientStream;
    public override int OverheadLength => 0;

    protected override async ValueTask SendPacketsAsync(IList<IpPacket> ipPackets)
    {
        if (IsDisposed) throw new ObjectDisposedException(VhLogger.FormatType(this));
        var cancellationToken = CancellationToken;

        // copy packets to buffer
        var buffer = _buffer;
        var bufferIndex = 0;

        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var ipPacket = ipPackets[i];
            var packetBytes = ipPacket.Buffer;

            // flush current buffer if this packet does not fit
            if (bufferIndex > 0 && bufferIndex + packetBytes.Length > buffer.Length) {
                await _clientStream.Stream.WriteAsync(buffer[..bufferIndex], cancellationToken).VhConfigureAwait();
                bufferIndex = 0;
            }

            // Write the packet directly if it does not fit in the buffer or there is only one packet
            if (packetBytes.Length > buffer.Length || ipPackets.Count == 1) {
                // send packet
                await _clientStream.Stream.WriteAsync(packetBytes, cancellationToken).VhConfigureAwait();
                continue;
            }

            packetBytes.Span.CopyTo(buffer.Span[bufferIndex..]);
            bufferIndex += packetBytes.Length;
        }

        // send remaining buffer
        if (bufferIndex > 0) {
            await _clientStream.Stream.WriteAsync(buffer[..bufferIndex], cancellationToken).VhConfigureAwait();
        }
    }

    protected override async Task StartReadTask()
    {
        var cancellationToken = CancellationToken;

        var streamPacketReader =
            new StreamPacketReader(_clientStream.Stream, TunnelDefaults.StreamPacketReaderBufferSize);

        // stop reading if State is not Connected (Such as getting the close request)
        while (!cancellationToken.IsCancellationRequested) {
            var ipPacket = await streamPacketReader.ReadAsync(cancellationToken).VhConfigureAwait();
            if (ipPacket == null) {
                VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel, "Packet stream ended. Terminating read task.");
                break;
            }

            OnPacketReceived(ipPacket);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            Stop();
            _clientStream.Dispose();
        }

        base.Dispose(disposing);
    }
}