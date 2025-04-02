using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Channels;
using VpnHood.Core.Tunneling.DatagramMessaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling;

public class Tunnel : IJob, IAsyncDisposable
{
    private readonly object _channelListLock = new();
    private readonly SemaphoreSlim _packetSentEvent = new(0);
    private readonly SemaphoreSlim _packetSenderSemaphore = new(0);
    private readonly HashSet<StreamProxyChannel> _streamProxyChannels = [];
    private readonly List<IDatagramChannel> _datagramChannels = [];
    private readonly Timer _speedMonitorTimer;
    private int _maxDatagramChannelCount;
    private Traffic _lastTraffic = new();
    private readonly Traffic _trafficUsage = new();
    private DateTime _lastSpeedUpdateTime = FastDateTime.Now;
    private readonly TimeSpan _speedTestThreshold = TimeSpan.FromSeconds(2);
    private readonly Channel<IPPacket> _sendChannel;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    public Traffic Speed { get; } = new();
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;
    public JobSection JobSection { get; } = new();
    public int Mtu { get; set; } = TunnelDefaults.Mtu;
    
    public Tunnel(TunnelOptions? options = null)
    {
        options ??= new TunnelOptions();
        _maxDatagramChannelCount = options.MaxDatagramChannelCount;
        _speedMonitorTimer = new Timer(_ => UpdateSpeed(), null, TimeSpan.Zero, _speedTestThreshold);
        _sendChannel = Channel.CreateBounded<IPPacket>(new BoundedChannelOptions(options.MaxQueueLength) {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        _ = SendEnqueuedPacketTask(options.MaxQueueLength);
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
        var trafficChanged = _lastTraffic != traffic;
        var duration = (FastDateTime.Now - _lastSpeedUpdateTime).TotalSeconds;
        if (duration < 1)
            return;

        Speed.Sent = (long)((traffic.Sent - _lastTraffic.Sent) / duration);
        Speed.Received = (long)((traffic.Received - _lastTraffic.Received) / duration);
        _lastSpeedUpdateTime = FastDateTime.Now;
        _lastTraffic = traffic.Clone();
        if (trafficChanged)
            LastActivityTime = FastDateTime.Now;
    }

    private bool IsChannelExists(IChannel channel)
    {
        lock (_channelListLock) {
            return channel is IDatagramChannel
                ? _datagramChannels.Contains(channel)
                : _streamProxyChannels.Contains(channel);
        }
    }

    public void AddChannel(IDatagramChannel datagramChannel)
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
            if (channel is IDatagramChannel datagramChannel) {
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

    private void Channel_OnPacketReceived(object sender, PacketReceivedEventArgs e)
    {
        if (_disposed)
            return;

        if (VhLogger.IsDiagnoseMode)
            PacketLogger.LogPackets(e.IpPackets, $"Packets received from a channel. ChannelId: {(sender as IChannel)?.ChannelId}");

        // check datagram message
        // performance critical; don't create another array by linq
        // performance critical; don't use Linq to check the message
        // ReSharper disable once LoopCanBeConvertedToQuery
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < e.IpPackets.Count; i++) {
            if (DatagramMessageHandler.IsDatagramMessage(e.IpPackets[i])) {
                e = new PacketReceivedEventArgs(
                    e.IpPackets.Where(x => !DatagramMessageHandler.IsDatagramMessage(x)).ToArray());
                break;
            }
        }

        try {
            PacketReceived?.Invoke(sender, e);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.DatagramChannel, ex,
                "Packets dropped! Error in processing channel received packets.");
        }
    }

    // Usually server use this method to send packets to the client
    public void SendPacketEnqueue(IPPacket ipPacket)
    {
        _sendChannel.Writer.TryWrite(ipPacket);
    }

    private async Task SendEnqueuedPacketTask(int maxQueueLength)
    {
        var packets = new List<IPPacket>(maxQueueLength);
        while (!_disposed) {
            // wait for new packets
            await _sendChannel.Reader.WaitToReadAsync(_cancellationTokenSource.Token);

            // dequeue all packets
            while (_sendChannel.Reader.TryRead(out var ipPacket))
                packets.Add(ipPacket);

            // send packets
            await SendPacketsAsync(packets);
        }
    }

    public async Task SendPacketsAsync(IList<IPPacket> ipPackets)
    {
        if (_disposed) 
            throw new ObjectDisposedException(nameof(Tunnel));

        // check is there any packet larger than MTU
        CheckMtu(ipPackets);

        // flush all packets to the same channel
        var channel = FindChannelForPackets(ipPackets);
        if (channel != null) {
            try {
                await channel.SendPacketAsync(ipPackets);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(GeneralEventId.DatagramChannel, ex,
                    "Could not send some packets via a channel. ChannelId: {ChannelId}, PacketCount: {PacketCount}",
                    channel.ChannelId, ipPackets.Count);

                RemoveChannel(channel);
            }
            return;
        }

        // Send each packet to the appropriate channel
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            channel = FindChannelForPacket(ipPackets[i]);
            try {
                await channel.SendPacketAsync(ipPackets[i]);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(GeneralEventId.DatagramChannel, ex,
                    "Could not send a packet via a channel. ChannelId: {ChannelId}, PacketCount: {PacketCount}",
                    channel.ChannelId, ipPackets.Count);
                RemoveChannel(channel);
            }
        }
    }

    public void SendPackets(IList<IPPacket> ipPackets)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tunnel));

        // check is there any packet larger than MTU
        CheckMtu(ipPackets);

        // flush all packets to the same channel
        var channel = FindChannelForPackets(ipPackets);
        if (channel != null) {
            try {
                channel.SendPacket(ipPackets);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(GeneralEventId.DatagramChannel, ex,
                    "Could not send some packets via a channel. ChannelId: {ChannelId}, PacketCount: {PacketCount}",
                    channel.ChannelId, ipPackets.Count);

                RemoveChannel(channel);
            }
            return;
        }

        // Send each packet to the appropriate channel
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            channel = FindChannelForPacket(ipPackets[i]);
            try {
                channel.SendPacket(ipPackets[i]);
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(GeneralEventId.DatagramChannel, ex,
                    "Could not send a packet via a channel. ChannelId: {ChannelId}, PacketCount: {PacketCount}",
                    channel.ChannelId, ipPackets.Count);

                RemoveChannel(channel);
            }
        }
    }

    private void CheckMtu(IList<IPPacket> ipPackets)
    {
        // check is there any packet larger than MTU
        var found = false;
        for (var i = 0; i < ipPackets.Count && !found; i++) 
            found = ipPackets[i].TotalPacketLength > Mtu;
        
        // no need to check if there is no large packet
        if (!found)
            return;

        // remove large packets and send PacketTooBig replies
        var bigPackets = ipPackets.Where(x=>x.TotalPacketLength >Mtu);
        foreach (var bigPacket in bigPackets) {
            try {
                ipPackets.Remove(bigPacket);
                VhLogger.Instance.LogWarning(
                    "Packet dropped! Packet is too big. " +
                    "MTU: {Mtu}, PacketLength: {PacketLength} Packet: {Packet}",
                    Mtu, bigPacket.TotalPacketLength, PacketLogger.Format(bigPacket));

                var replyPacket = PacketBuilder.BuildPacketTooBigReply(bigPacket, (ushort)Mtu);
                PacketReceived?.Invoke(this, new PacketReceivedEventArgs([replyPacket]));
            }
            catch (Exception ex) {
                VhLogger.Instance.LogError(GeneralEventId.DatagramChannel, ex,
                    "Packets dropped! Error in sending BuildPacketTooBigReply.");
            }
        }
    }

    private IDatagramChannel? FindChannelForPackets(IList<IPPacket> ipPackets)
    {
        IDatagramChannel? channel = null;

        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var packetChannel = FindChannelForPacket(ipPackets[i]);
            channel ??= packetChannel;
            if (channel != packetChannel)
                return null; // different channels
        }

        return channel;
    }

    private IDatagramChannel FindChannelForPacket(IPPacket ipPacket)
    {
        // send packets directly if there is only one channel
        lock (_datagramChannels) {
            var channelCount = _datagramChannels.Count;
            return channelCount switch {
                0 => throw new Exception("There is no DatagramChannel to send packets."),
                1 => _datagramChannels[0],
                _ => ipPacket.Protocol switch {
                    // select channel by tcp source port
                    ProtocolType.Tcp => _datagramChannels[ipPacket.ExtractTcp().SourcePort % channelCount],

                    // select channel by udp source port
                    ProtocolType.Udp => _datagramChannels[ipPacket.ExtractUdp().SourcePort % _datagramChannels.Count],

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

        // release worker threads (make sure to release all semaphores)
        _packetSenderSemaphore.Release(MaxDatagramChannelCount * 10);
        _packetSentEvent.Release();

        // dispose all channels
        await Task.WhenAll(disposeTasks).VhConfigureAwait();
    }
}