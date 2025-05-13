using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Collections;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling;

public abstract class PacketProxy : IPacketSenderQueued, ITimeoutItem
{
    private bool _isSending;
    private readonly IpPacket[] _singlePacketList = new IpPacket[1];
    private readonly PacketReceivedEventArgs _packetReceivedEventArgs = new([]);
    private readonly PacketSenderChannel _senderChannel;
    protected abstract Task SendPacketAsync(IpPacket ipPacket);
    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public DateTime LastUsedTime { get; set; } = DateTime.MinValue;
    public bool IsBusy => _isSending || _senderChannel.QueueLength > 0;
    public bool Disposed { get; private set; }


    protected PacketProxy(int queueCapacity, bool autoDisposeSentPackets)
    {
        _senderChannel = new PacketSenderChannel(SendPacketInternalAsync, queueCapacity, autoDisposeSentPackets);
    }

    public void SendPacketQueued(IpPacket ipPacket)
    {
        PacketLogger.LogPacket(ipPacket, $"Delegating packet to host via {GetType().Name}.");
        _senderChannel.SendPacketQueued(ipPacket);
    }

    private async Task SendPacketInternalAsync(IList<IpPacket> ipPackets)
    {
        try {
            _isSending = true;
            LastUsedTime = FastDateTime.Now;

            // ReSharper disable once ForCanBeConvertedToForeach
            for (var index = 0; index < ipPackets.Count; index++) {
                var ipPacket = ipPackets[index];
                try {
                    PacketLogger.LogPacket(ipPacket, "Sending packet via PacketProxy.");
                    await SendPacketAsync(ipPacket);
                }
                catch (Exception ex) {
                    PacketLogger.LogPacket(ipPacket, $"Error in sending packet using {GetType().Name}.", exception: ex);
                }
            }
        }
        finally {
            _isSending = false;

        }
    }

    protected void OnPacketReceived(IpPacket ipPacket)
    {
        _singlePacketList[0] = ipPacket;
        OnPacketReceived(_singlePacketList);
    }

    protected void OnPacketReceived(IList<IpPacket> ipPackets)
    {
        LastUsedTime = FastDateTime.Now;
        // it is not thread safe and caller should be careful
        _packetReceivedEventArgs.IpPackets = ipPackets;
        try {
            PacketLogger.LogPackets(ipPackets, "Received packets via PacketProxy.");
            PacketReceived?.Invoke(this, _packetReceivedEventArgs);
        }
        catch (Exception ex) {
            PacketLogger.LogPacket(ipPackets[0],
                "Error while invoking the received packets event in PacketProxy.", exception: ex);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Disposed) return;

        // Dispose managed resources
        if (disposing) {
            VhUtils.TryInvoke("Dispose Sender Channel", () =>
                _senderChannel.DisposeAsync().AsTask().Wait());

            PacketReceived = null;
        }

        // Dispose unmanaged resources if any
        Disposed = true;
    }
}