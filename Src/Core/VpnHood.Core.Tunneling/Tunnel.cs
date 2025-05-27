using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Tunneling;

public class Tunnel : PassthroughPacketTransport
{
    private readonly Timer _speedMonitorTimer;
    private Traffic _lastTraffic = new();
    private DateTime _lastSpeedUpdateTime = FastDateTime.Now;
    private readonly TimeSpan _speedTestThreshold = TimeSpan.FromSeconds(2);
    private readonly ChannelManager _channelManager;
    private Traffic _speed = new();
    private readonly object _speedLock = new();

    public void AddChannel(IChannel channel) => _channelManager.AddChannel(channel);
    public DateTime LastActivityTime { get; private set; }
    public Traffic Traffic => _channelManager.Traffic;
    public int PacketChannelCount => _channelManager.PacketChannelCount;
    public int StreamProxyChannelCount => _channelManager.ProxyChannelCount;
    public void RemoveAllPacketChannels() => _channelManager.RemoveAllPacketChannels();

    public int MaxPacketChannelCount {
        get => _channelManager.MaxPacketChannelCount;
        set => _channelManager.MaxPacketChannelCount = value;
    }

    public int RemoteMtu { get; set; } = TunnelDefaults.MaxPacketSize;

    public Traffic Speed {
        get {
            lock (_speedLock) return _speed;
        }
        private set {
            lock (_speedLock) _speed = value;
        }
    }

    public Tunnel(TunnelOptions options)
        : base(options.AutoDisposePackets)
    {
        _channelManager = new ChannelManager(options.MaxPacketChannelCount, Channel_OnPacketReceived);
        _speedMonitorTimer = new Timer(_ => UpdateSpeed(), null, TimeSpan.Zero, _speedTestThreshold);
    }

    private void UpdateSpeed()
    {
        if (IsDisposed)
            return;

        lock (_speedLock) {
            var traffic = _channelManager.Traffic;
            var duration = (FastDateTime.Now - _lastSpeedUpdateTime).TotalSeconds;
            if (duration < 1)
                return;

            var sendSpeed = (long)((traffic.Sent - _lastTraffic.Sent) / duration);
            var receivedSpeed = (long)((traffic.Received - _lastTraffic.Received) / duration);
            Speed = new Traffic(sendSpeed, receivedSpeed);
            _lastSpeedUpdateTime = FastDateTime.Now;

            // update traffic usage & last traffic if changed
            if (_lastTraffic != traffic) {
                LastActivityTime = FastDateTime.Now;
                _lastTraffic = traffic;
            }
        }
    }
    
    private void Channel_OnPacketReceived(object sender, IpPacket ipPacket)
    {
        OnPacketReceived(ipPacket);
    }

    // it is not thread-safe
    protected override void SendPacket(IpPacket ipPacket)
    {
        // flush all packets to the same channel
        var channel = FindChannelForPacket(ipPacket);

        // check is there any packet larger than MTU
        VerifyMtu(ipPacket, channel.OverheadLength);

        // send packet
        channel.SendPacketQueued(ipPacket);
    }

    private void VerifyMtu(IpPacket ipPacket, int overheadSize)
    {
        // use RemoteMtu if the channel is streamed
        if (ipPacket.PacketLength + overheadSize <= RemoteMtu)
            return;

        var replyPacket = PacketBuilder.BuildIcmpPacketTooBigReply(ipPacket, (ushort)RemoteMtu);
        OnPacketReceived(replyPacket);
        throw new Exception(
            $"The packet length is larger than MTU. PacketLength: {ipPacket.PacketLength}, MTU: {RemoteMtu}.");
    }

    private IPacketChannel FindChannelForPacket(IpPacket ipPacket)
    {
        // remove channel if it is not connected
        var channel = FindChannelForPacketInternal(ipPacket);
        while (channel.State != PacketChannelState.Connected) {
            _channelManager.CleanupChannels();
            channel = FindChannelForPacketInternal(ipPacket);
        }

        return channel;
    }

    private IPacketChannel FindChannelForPacketInternal(IpPacket ipPacket)
    {
        // find channel by protocol
        var channelCount = _channelManager.PacketChannelCount;
        var channelIndex = channelCount switch {
            0 => throw new Exception("There is no PacketChannel to send packets."),
            1 => 0,
            _ => ipPacket.Protocol switch {
                // select channel by tcp source port
                IpProtocol.Tcp => ipPacket.ExtractTcp().SourcePort % channelCount,

                // select channel by udp source port
                IpProtocol.Udp => ipPacket.ExtractUdp().SourcePort % channelCount,

                // select the first channel for other protocols
                _ => 0
            }
        };

        return _channelManager.GetPacketChannel(channelIndex);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _channelManager.Dispose();
            _speedMonitorTimer.Dispose();
            Speed = new Traffic();
        }
        base.Dispose(disposing);
    }
}