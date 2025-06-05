using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.Packets.Extensions;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Core.Tunneling;

public class Tunnel : PassthroughPacketTransport
{
    private Traffic _lastTraffic = new();
    private DateTime _lastSpeedUpdateTime = FastDateTime.Now;
    private readonly TimeSpan _speedometerThreshold = TimeSpan.FromSeconds(2);
    private readonly ChannelManager _channelManager;
    private Traffic _speed = new();
    private readonly object _speedLock = new();
    private readonly Job? _speedometerJob;

    public void AddChannel(IChannel channel) => _channelManager.AddChannel(channel);
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;
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
            if (IsDisposed) 
                return new Traffic();

            if (_speedometerJob == null)
                UpdateSpeed(CancellationToken.None);

            lock (_speedLock) {
                return _speed;
            }
        }
    }

    public Tunnel(TunnelOptions options)
    {
        _channelManager = new ChannelManager(options.MaxPacketChannelCount, Channel_OnPacketReceived);
        _speedometerJob = options.UseSpeedometerTimer
            ? new Job(UpdateSpeed, _speedometerThreshold, "TunnelSpeedometer")
            : null;
    }

    private ValueTask UpdateSpeed(CancellationToken cancellationToken)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(Tunnel));

        lock (_speedLock) {
            var now = FastDateTime.Now;
            var traffic = _channelManager.Traffic;
            var duration = (now - _lastSpeedUpdateTime).TotalSeconds;
            if (duration < 1)
                return default;

            var sendSpeed = (long)((traffic.Sent - _lastTraffic.Sent) / duration);
            var receivedSpeed = (long)((traffic.Received - _lastTraffic.Received) / duration);
            _speed = new Traffic(sendSpeed, receivedSpeed);
            _lastSpeedUpdateTime = now;

            // update traffic usage & last traffic if changed
            if (_lastTraffic != traffic) {
                LastActivityTime = now;
                _lastTraffic = traffic;
            }
        }

        return default;
    }

    private void Channel_OnPacketReceived(object? sender, IpPacket ipPacket)
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
            0 => throw new Exception("No available PacketChannel to send packets."),
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

    protected override void DisposeManaged()
    {
        _channelManager.Dispose();
        _speedometerJob?.Dispose();
        _speed = new Traffic();

        base.DisposeManaged();
    }
}