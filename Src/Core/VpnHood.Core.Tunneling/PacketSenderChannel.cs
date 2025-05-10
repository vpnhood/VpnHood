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
    private readonly IPacketSender _packetSender;
    private readonly int _maxQueueLength;
    private readonly Task _sendingTask;

    public PacketSenderChannel(IPacketSender packetSender, int maxQueueLength)
    {
        _packetSender = packetSender;
        _maxQueueLength = maxQueueLength;
        _sendChannel = Channel.CreateBounded<IpPacket>(new BoundedChannelOptions(maxQueueLength) {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

        _sendingTask = StartSendingPacketsAsync();
    }

    private async Task StartSendingPacketsAsync()
    {
        var ipPackets = new List<IpPacket>(_maxQueueLength);
        while (await _sendChannel.Reader.WaitToReadAsync()) {
            
            // dequeue all packets
            while (_sendChannel.Reader.TryRead(out var ipPacket) && ipPackets.Count < ipPackets.Capacity)
                ipPackets.Add(ipPacket);

            // send packets
            try {
                await _packetSender.SendPacketsAsync(ipPackets);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(ex, "Error in sending packets via channel.");
            }

            // All packets belong to queue worker, so we can dispose them
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < ipPackets.Count; i++)
                ipPackets[i].Dispose();

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

        _sendChannel.Writer.Complete();
        await _sendingTask;

        _disposed = true;
    }
}
