using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling;

public class PacketSenderChannel : IAsyncDisposable
{
    private bool _disposed;
    private readonly Channel<IpPacket> _sendChannel;
    private readonly PacketsHandlerAsync _sendPacketsHandlerAsync;
    private readonly int _queueCapacity;
    private readonly bool _autoDisposeSentPackets;
    private readonly Task _sendingTask;
    public int QueueLength => _sendChannel.Reader.Count;
    public delegate Task PacketsHandlerAsync(IList<IpPacket> ipPackets);

    public PacketSenderChannel(PacketsHandlerAsync sendPacketsHandlerAsync, int queueCapacity,
        bool autoDisposeSentPackets)
    {
        _sendPacketsHandlerAsync = sendPacketsHandlerAsync;
        _queueCapacity = queueCapacity;
        _autoDisposeSentPackets = autoDisposeSentPackets;
        _sendChannel = Channel.CreateBounded<IpPacket>(new BoundedChannelOptions(queueCapacity) {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

        _sendingTask = StartSendingPacketsAsync();
    }

    private async Task StartSendingPacketsAsync()
    {
        var ipPackets = new List<IpPacket>(_queueCapacity);
        while (await _sendChannel.Reader.WaitToReadAsync()) {
            ipPackets.Clear();

            // dequeue all packets
            while (_sendChannel.Reader.TryRead(out var ipPacket) && ipPackets.Count < ipPackets.Capacity)
                ipPackets.Add(ipPacket);

            // send packets
            try {
                await _sendPacketsHandlerAsync(ipPackets);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error in sending packets via channel.");
            }
            finally {
                if (_autoDisposeSentPackets)
                    ipPackets.DisposeAllPackets();
            }
        }

        // dispose remaining packets
        if (_autoDisposeSentPackets) {
            while (_sendChannel.Reader.TryRead(out var ipPacket))
                ipPacket.Dispose();
        }
    }

    public void SendPacketQueued(IpPacket ipPacket)
    {
        var result = _sendChannel.Writer.TryWrite(ipPacket);
        if (!result) {
            PacketLogger.LogPacket(ipPacket, "Dropping packet. Send queue full.", logLevel: LogLevel.Debug);
            if (_autoDisposeSentPackets)
                ipPacket.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _sendChannel.Writer.TryComplete();
        await _sendingTask;

        _disposed = true;
    }
}
