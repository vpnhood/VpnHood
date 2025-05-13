using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets.VhPackets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling;

public class PacketSenderChannel : IAsyncDisposable
{
    private bool _disposed;
    private readonly Channel<IpPacket> _sendChannel;
    private readonly PacketsHandlerAsync _sendPacketsHandlerAsync;
    private readonly int _queueCapacity;
    private readonly Task _sendingTask;
    public int QueueLength => _sendChannel.Reader.Count;
    public delegate Task PacketsHandlerAsync(IList<IpPacket> ipPackets);

    public PacketSenderChannel(PacketsHandlerAsync sendPacketsHandlerAsync, int queueCapacity)
    {
        _sendPacketsHandlerAsync = sendPacketsHandlerAsync;
        _queueCapacity = queueCapacity;
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

            // All packets belong to queue worker, so we can dispose them
            // ReSharper disable once ForCanBeConvertedToForeach
            ipPackets.DisposeAllPackets();
            ipPackets.Clear();
        }
    }

    public bool SendPacketEnqueue(IpPacket ipPacket)
    {
        var result = _sendChannel.Writer.TryWrite(ipPacket);
        if (!result) {
            PacketLogger.LogPacket(ipPacket, "Dropping packet. Send queue full.", logLevel: LogLevel.Debug);
            ipPacket.Dispose(); // Only dispose if enqueue failed
        }
        return result;
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
