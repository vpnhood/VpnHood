using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.DatagramMessaging;

namespace VpnHood.Core.Tunneling;

public class Tunnel : PassthroughPacketTransport, IJob
{
    private readonly object _channelListLock = new();
    private readonly HashSet<StreamProxyChannel> _streamProxyChannels = [];
    private readonly List<IPacketChannel> _datagramChannels = [];
    private readonly Timer _speedMonitorTimer;
    private int _maxDatagramChannelCount;
    private Traffic _lastTraffic = new();
    private readonly Traffic _trafficUsage = new();
    private DateTime _lastSpeedUpdateTime = FastDateTime.Now;
    private readonly TimeSpan _speedTestThreshold = TimeSpan.FromSeconds(2);
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public Traffic Speed { get; } = new();
    public JobSection JobSection { get; } = new();
    public int Mtu { get; set; } = TunnelDefaults.Mtu;
    public int RemoteMtu { get; set; } = TunnelDefaults.MtuRemote;

    public Tunnel(TunnelOptions options)
    : base(options.AutoDisposePackets)
    {
        _maxDatagramChannelCount = options.MaxDatagramChannelCount;
        _speedMonitorTimer = new Timer(_ => UpdateSpeed(), null, TimeSpan.Zero, _speedTestThreshold);
        JobRunner.Default.Add(this);
    }

    public int StreamProxyChannelCount {
        get {
            lock (_channelListLock)
                return _streamProxyChannels.Count;
        }
    }

    public int DatagramChannelCount {
        get {
            lock (_channelListLock)
                return _datagramChannels.Count;
        }
    }

    public bool IsUdpMode => UdpChannel != null;

    public UdpChannel? UdpChannel {
        get {
            lock (_channelListLock)
                return (UdpChannel?)_datagramChannels.FirstOrDefault(x => x is UdpChannel);
        }
    }

    public Traffic Traffic {
        get {
            lock (_channelListLock) {
                return new Traffic {
                    Sent = _trafficUsage.Sent +
                           _streamProxyChannels.Sum(x => x.Traffic.Sent) +
                           _datagramChannels.Sum(x => x.Traffic.Sent),
                    Received = _trafficUsage.Received +
                               _streamProxyChannels.Sum(x => x.Traffic.Received) +
                               _datagramChannels.Sum(x => x.Traffic.Received)
                };
            }
        }
    }

    public int MaxDatagramChannelCount {
        get => _maxDatagramChannelCount;
        set {
            if (_maxDatagramChannelCount < 1)
                throw new ArgumentException("Value must equals or greater than 1", nameof(MaxDatagramChannelCount));
            _maxDatagramChannelCount = value;
        }
    }

    private void UpdateSpeed()
    {
        if (_disposed)
            return;

        if (FastDateTime.Now - _lastSpeedUpdateTime < _speedTestThreshold)
            return;

        var traffic = Traffic;
        var duration = (FastDateTime.Now - _lastSpeedUpdateTime).TotalSeconds;
        if (duration < 1)
            return;

        Speed.Sent = (long)((traffic.Sent - _lastTraffic.Sent) / duration);
        Speed.Received = (long)((traffic.Received - _lastTraffic.Received) / duration);
        _lastSpeedUpdateTime = FastDateTime.Now;
        _lastTraffic = traffic.Clone();
    }

    private bool IsChannelExists(IChannel channel)
    {
        lock (_channelListLock) {
            return channel is IPacketChannel
                ? _datagramChannels.Contains(channel)
                : _streamProxyChannels.Contains(channel);
        }
    }

    public void AddChannel(IPacketChannel datagramChannel)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tunnel));

        //should not be called in lock; its behaviour is unexpected
        datagramChannel.PacketReceived += Channel_OnPacketReceived;
        datagramChannel.Start();

        // add to channel list
        lock (_channelListLock) {
            if (_datagramChannels.Contains(datagramChannel))
                throw new Exception("the DatagramChannel already exists in the collection.");

            _datagramChannels.Add(datagramChannel);
            VhLogger.Instance.LogInformation(GeneralEventId.DatagramChannel,
                "A DatagramChannel has been added. ChannelId: {ChannelId}, ChannelCount: {ChannelCount}, ChannelType: {ChannelType}",
                datagramChannel.ChannelId, _datagramChannels.Count, datagramChannel.GetType().Name);

            // remove additional Datagram channels
            while (_datagramChannels.Count > MaxDatagramChannelCount) {
                VhLogger.Instance.LogInformation(GeneralEventId.DatagramChannel,
                    "Removing an exceeded DatagramChannel. ChannelId: {ChannelId}, ChannelCount: {ChannelCount}",
                    datagramChannel.ChannelId, _datagramChannels.Count);

                RemoveChannel(_datagramChannels[0]);
            }

            // UdpChannels and StreamChannels can not be added together
            foreach (var channel in _datagramChannels.Where(x => x.IsStream != datagramChannel.IsStream).ToArray())
                RemoveChannel(channel);
        }
    }

    public void AddChannel(StreamProxyChannel channel)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tunnel));

        // should not be called in lock; its behaviour is unexpected
        channel.Start();

        // add channel
        lock (_channelListLock)
            if (!_streamProxyChannels.Add(channel))
                throw new Exception($"Could not add {channel.GetType()}. ChannelId: {channel.ChannelId}");

        VhLogger.Instance.LogDebug(GeneralEventId.StreamProxyChannel,
            "A StreamProxyChannel has been added. ChannelId: {ChannelId}, ChannelCount: {ChannelCount}",
            channel.ChannelId, StreamProxyChannelCount);
    }

    private void RemoveChannel(IChannel channel)
    {
        if (!IsChannelExists(channel))
            return; // channel already removed or does not exist

        lock (_channelListLock) {
            if (channel is IPacketChannel datagramChannel) {
                _datagramChannels.Remove(datagramChannel);
                VhLogger.Instance.LogDebug(GeneralEventId.DatagramChannel,
                    "A DatagramChannel has been removed. Channel: {Channel}, ChannelId: {ChannelId}, " +
                    "ChannelCount: {ChannelCount}, Connected: {Connected}",
                    VhLogger.FormatType(channel), channel.ChannelId, _datagramChannels.Count, channel.Connected);
            }
            else if (channel is StreamProxyChannel streamProxyChannel) {
                _streamProxyChannels.Remove(streamProxyChannel);
                VhLogger.Instance.LogDebug(GeneralEventId.StreamProxyChannel,
                    "A StreamProxyChannel has been removed. Channel: {Channel}, ChannelId: {ChannelId}, " +
                    "ChannelCount: {ChannelCount}, Connected: {Connected}",
                    VhLogger.FormatType(channel), channel.ChannelId, _streamProxyChannels.Count, channel.Connected);
            }
            else
                throw new ArgumentOutOfRangeException(nameof(channel), "Unknown Channel.");
        }

        // clean up channel
        _trafficUsage.Add(channel.Traffic);
        channel.DisposeAsync();
    }

    private void Channel_OnPacketReceived(object sender, IpPacket ipPacket)
    {
        if (_disposed)
            return;

        // check datagram message
        if (DatagramMessageHandler.IsDatagramMessage(ipPacket)) {
            ipPacket.Dispose();
            return;
        }

        OnPacketReceived(ipPacket);
    }

    // it is not thread-safe
    protected override void SendPacket(IpPacket ipPacket)
    {
        // check is there any packet larger than MTU
        VerifyMtu(ipPacket);

        // flush all packets to the same channel
        var channel = FindChannelForPacket(ipPacket);
        channel.SendPacketQueued(ipPacket);
    }

    private void VerifyMtu(IpPacket ipPacket)
    {
        // use RemoteMtu if the channel is streamed
        var mtu = IsUdpMode ? Mtu : RemoteMtu;
        if (ipPacket.PacketLength <= mtu)
            return;

        var replyPacket = PacketBuilder.BuildIcmpPacketTooBigReply(ipPacket, (ushort)mtu);
        OnPacketReceived(replyPacket);
        throw new Exception(
            $"The packet is larger than MTU. PacketLength: {ipPacket.PacketLength}, MTU: {mtu}.");
    }

    private IPacketChannel FindChannelForPacket(IpPacket ipPacket)
    {
        // remove channel if it is not connected
        var channel = FindChannelForPacketInternal(ipPacket);
        if (!channel.Connected) {
            RemoveChannel(channel);
            return FindChannelForPacketInternal(ipPacket);
        }

        return channel;
    }

    private IPacketChannel FindChannelForPacketInternal(IpPacket ipPacket)
    {
        // send packets directly if there is only one channel
        lock (_datagramChannels) {
            var channelCount = _datagramChannels.Count;
            return channelCount switch {
                0 => throw new Exception("There is no DatagramChannel to send packets."),
                1 => _datagramChannels[0],
                _ => ipPacket.Protocol switch {
                    // select channel by tcp source port
                    IpProtocol.Tcp => _datagramChannels[ipPacket.ExtractTcp().SourcePort % channelCount],

                    // select channel by udp source port
                    IpProtocol.Udp => _datagramChannels[ipPacket.ExtractUdp().SourcePort % _datagramChannels.Count],

                    // select the first channel for other protocols
                    _ => _datagramChannels[0]
                }
            };
        }
    }

    public Task RunJob()
    {
        // remove disconnected channels
        lock (_channelListLock) {
            foreach (var channel in _streamProxyChannels.Where(x => !x.Connected).ToArray())
                RemoveChannel(channel);

            foreach (var channel in _datagramChannels.Where(x => !x.Connected).ToArray())
                RemoveChannel(channel);
        }

        return Task.CompletedTask;
    }


    private readonly AsyncLock _disposeLock = new();
    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        using var lockResult = await _disposeLock.LockAsync().VhConfigureAwait();
        if (_disposed) return;
        _disposed = true;
        _cancellationTokenSource.Cancel();

        // make sure to call RemoveChannel to perform proper clean up such as setting _sentByteCount and _receivedByteCount 
        var disposeTasks = new List<Task>();
        lock (_channelListLock) {
            disposeTasks.AddRange(_streamProxyChannels.Select(channel => channel.DisposeAsync(false).AsTask()));
            disposeTasks.AddRange(_datagramChannels.Select(channel => channel.DisposeAsync(false).AsTask()));
        }

        // Stop speed monitor
        await _speedMonitorTimer.DisposeAsync().VhConfigureAwait();
        Speed.Sent = 0;
        Speed.Received = 0;

        // dispose all channels
        await Task.WhenAll(disposeTasks).VhConfigureAwait();
    }
}