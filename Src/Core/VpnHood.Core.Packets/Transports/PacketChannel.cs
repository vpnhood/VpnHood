using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Packets.Transports;

public abstract class PacketChannel(PacketChannelOptions channelOptions, bool singleMode)
    : PacketSender(channelOptions, singleMode), IPacketChannel
{
    public DateTime LastReceivedTime { get; private set; }
    public DateTime LastActivityTime => LastReceivedTime > LastSentTime ? LastReceivedTime : FastDateTime.Now;
    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    private readonly PacketReceivedEventArgs _packetReceivedEventArgs = new(new IpPacket[1]);

    protected void OnPacketReceived(PacketReceivedEventArgs arg)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < arg.IpPackets.Count; i++) {
            var ipPacket = arg.IpPackets[i];
            OnPacketReceived(ipPacket);
        }
    }

    protected void OnPacketReceived(IpPacket ipPacket)
    {
        try {
            lock (_packetReceivedEventArgs) { //todo: remove by packet
                LastReceivedTime = FastDateTime.Now;
                _packetReceivedEventArgs.IpPackets[0] = ipPacket;
                LogPacket(ipPacket, $"Received a packet from {VhLogger.FormatType(this)}.");
                PacketReceived?.Invoke(this, _packetReceivedEventArgs);
            }
        }
        catch (Exception ex) {
            LogPacket(ipPacket, "Error while invoking the received packets event in PacketProxy.", exception: ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            PacketReceived = null;
        }

        base.Dispose(disposing);
    }
}