using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Collections;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.DatagramMessaging;

namespace VpnHood.Tunneling;

public class Tunnel : IDisposable
{
    private readonly object _channelListLock = new();
    private readonly int _maxQueueLength = 100;
    private readonly int _mtuNoFragment = TunnelUtil.MtuWithoutFragmentation;
    private readonly int _mtuWithFragment = TunnelUtil.MtuWithFragmentation;
    private readonly Queue<IPPacket> _packetQueue = new();
    private readonly SemaphoreSlim _packetSentEvent = new(0);
    private readonly SemaphoreSlim _packetSenderSemaphore = new(0);
    private readonly HashSet<TcpProxyChannel> _tcpProxyChannels = new();
    private readonly Timer _speedMonitorTimer;
    private bool _disposed;
    private long _lastReceivedByteCount;
    private long _lastSentByteCount;
    private int _maxDatagramChannelCount;
    private long _receivedByteCount;
    private long _sentByteCount;
    private readonly TimeSpan _datagramPacketTimeout = TimeSpan.FromSeconds(100);
    private DateTime _lastSpeedUpdateTime = FastDateTime.Now;
    private readonly TimeSpan _speedTestThreshold = TimeSpan.FromSeconds(2);
    private readonly TimeoutDictionary<IChannel, TimeoutItem<IChannel>> _closePendingChannels = new(TimeSpan.FromSeconds(30));

    public event EventHandler<ChannelPacketReceivedEventArgs>? OnPacketReceived;
    public event EventHandler<ChannelEventArgs>? OnChannelAdded;
    public event EventHandler<ChannelEventArgs>? OnChannelRemoved;
    public long SendSpeed { get; private set; }
    public long ReceiveSpeed { get; private set; }
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;
    public IDatagramChannel[] DatagramChannels { get; private set; } = Array.Empty<IDatagramChannel>();

    public Tunnel(TunnelOptions? options = null)
    {
        options ??= new TunnelOptions();
        _maxDatagramChannelCount = options.MaxDatagramChannelCount;
        _speedMonitorTimer = new Timer(_ => UpdateSpeed(), null, TimeSpan.Zero, _speedTestThreshold);
    }

    public int TcpProxyChannelCount
    {
        get
        {
            lock (_channelListLock)
                return _tcpProxyChannels.Count;
        }
    }

    public long ReceivedByteCount
    {
        get
        {
            lock (_channelListLock)
            {
                return _receivedByteCount + _tcpProxyChannels.Sum(x => x.ReceivedByteCount) +
                       DatagramChannels.Sum(x => x.ReceivedByteCount);
            }
        }
    }

    public long SentByteCount
    {
        get
        {
            lock (_channelListLock)
            {
                return _sentByteCount + _tcpProxyChannels.Sum(x => x.SentByteCount) +
                       DatagramChannels.Sum(x => x.SentByteCount);
            }
        }
    }

    public int MaxDatagramChannelCount
    {
        get => _maxDatagramChannelCount;
        set
        {
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

        var sentByteCount = SentByteCount;
        var receivedByteCount = ReceivedByteCount;
        var trafficChanged = _lastSentByteCount != sentByteCount || _lastReceivedByteCount != receivedByteCount;
        var duration = (FastDateTime.Now - _lastSpeedUpdateTime).TotalSeconds;

        SendSpeed = (int)((sentByteCount - _lastSentByteCount) / duration);
        ReceiveSpeed = (int)((receivedByteCount - _lastReceivedByteCount) / duration);

        _lastSpeedUpdateTime = FastDateTime.Now;
        _lastSentByteCount = sentByteCount;
        _lastReceivedByteCount = receivedByteCount;
        if (trafficChanged)
            LastActivityTime = FastDateTime.Now;
    }

    private bool IsChannelExists(IChannel channel)
    {
        lock (_channelListLock)
        {
            return channel is IDatagramChannel
                ? DatagramChannels.Contains(channel)
                : _tcpProxyChannels.Contains(channel);
        }
    }

    public void AddChannel(IDatagramChannel datagramChannel)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tunnel));

        // add to channel list
        lock (_channelListLock)
        {
            if (DatagramChannels.Contains(datagramChannel))
                throw new Exception("the DatagramChannel already exists in the collection.");

            datagramChannel.OnPacketReceived += Channel_OnPacketReceived;
            DatagramChannels = DatagramChannels.Concat(new[] { datagramChannel }).ToArray();
            VhLogger.Instance.LogInformation(GeneralEventId.DatagramChannel,
                "A DatagramChannel has been added. ChannelCount: {ChannelCount}", DatagramChannels.Length);

            // remove additional Datagram channels
            while (DatagramChannels.Length > MaxDatagramChannelCount)
            {
                VhLogger.Instance.LogInformation(GeneralEventId.DatagramChannel, 
                    "Removing an exceeded DatagramChannel. ChannelCount: {ChannelCount}", DatagramChannels.Length);

                RemoveChannel(DatagramChannels[0]);
            }
        }

        // register finish
        datagramChannel.OnFinished += Channel_OnFinished;

        // notify channel has been added; must before channel.Start because channel may be removed immediately after channel.Start
        OnChannelAdded?.Invoke(this, new ChannelEventArgs(datagramChannel));

        //should not be called in lock; its behaviour is unexpected
        _ = datagramChannel.Start();

        //  SendPacketTask after starting the channel and outside of the lock
        _ = SendPacketTask(datagramChannel);
    }

    public void AddChannel(TcpProxyChannel channel)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Tunnel));

        // add channel
        lock (_channelListLock)
        {
            if (_tcpProxyChannels.Contains(channel))
                throw new Exception($"{nameof(channel)} already exists in the collection.");
            _tcpProxyChannels.Add(channel);
        }

        VhLogger.Instance.LogInformation(GeneralEventId.TcpProxyChannel,
            "A TcpProxyChannel has been added. ChannelCount: {ChannelCount}", TcpProxyChannelCount);

        // register finish
        channel.OnFinished += Channel_OnFinished;

        // notify channel has been added; must before channel.Start because channel may be removed immediately after channel.Start
        OnChannelAdded?.Invoke(this, new ChannelEventArgs(channel));

        // should not be called in lock; its behaviour is unexpected
        _ = channel.Start();
    }

    public void RemoveChannel(IChannel channel)
    {
        if (!IsChannelExists(channel))
            return; // channel already removed or does not exist

        lock (_channelListLock)
        {
            if (channel is IDatagramChannel)
            {
                DatagramChannels = DatagramChannels.Where(x => x != channel).ToArray();
                VhLogger.Instance.LogInformation(GeneralEventId.DatagramChannel,
                    "A DatagramChannel has been removed. Channel: {Channel}, ChannelCount: {ChannelCount}, Connected: {Connected}, ClosePending: {ClosePending}",
                    VhLogger.FormatTypeName(channel), DatagramChannels.Length, channel.Connected, channel.IsClosePending);
            }
            else if (channel is TcpProxyChannel tcpProxyChannel)
            {
                _tcpProxyChannels.Remove(tcpProxyChannel);
                VhLogger.Instance.LogInformation(GeneralEventId.TcpProxyChannel,
                    "A TcpProxyChannel has been removed. Channel: {Channel}, ChannelCount: {ChannelCount}, Connected: {Connected}, ClosePending: {ClosePending}",
                    VhLogger.FormatTypeName(channel), _tcpProxyChannels.Count, channel.Connected, channel.IsClosePending);
            }
            else
                throw new ArgumentOutOfRangeException(nameof(channel), "Unknown Channel.");
        }

        // dispose or close-pending
        // ReSharper disable once MergeIntoPattern
        if (channel.Connected && channel.IsClosePending)
            _closePendingChannels.TryAdd(channel, new TimeoutItem<IChannel>(channel, true));
        else
            channel.Dispose();

        // clean up tunnel
        _sentByteCount += channel.SentByteCount;
        _receivedByteCount += channel.ReceivedByteCount;
        channel.OnFinished -= Channel_OnFinished;

        // notify channel has been removed
        OnChannelRemoved?.Invoke(this, new ChannelEventArgs(channel));
    }

    private void Channel_OnFinished(object sender, EventArgs e)
    {
        if (_disposed)
            return;

        RemoveChannel((IChannel)sender);
    }

    private void Channel_OnPacketReceived(object sender, ChannelPacketReceivedEventArgs e)
    {
        if (_disposed)
            return;

        if (VhLogger.IsDiagnoseMode)
            PacketUtil.LogPackets(e.IpPackets, $"Packets received from {nameof(Tunnel)}.");

        // check datagram message
        // performance critical; don't create another array by linq
        if (e.IpPackets.Any(DatagramMessageHandler.IsDatagramMessage))
            e = new ChannelPacketReceivedEventArgs(
                e.IpPackets.Where(x => !DatagramMessageHandler.IsDatagramMessage(x)).ToArray(), e.Channel);

        try
        {
            OnPacketReceived?.Invoke(sender, e);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(GeneralEventId.DatagramChannel, ex, "Packets dropped! Error in processing channel received packets.");
        }
    }

    public Task SendPacket(IPPacket ipPacket)
    {
        return SendPackets(new[] { ipPacket });
    }

    public async Task SendPackets(IEnumerable<IPPacket> ipPackets)
    {
        var dateTime = FastDateTime.Now;
        if (_disposed) throw new ObjectDisposedException(nameof(Tunnel));
        
        // waiting for a space in the packetQueue; the Inconsistently is not important. synchronization may lead to dead-lock
        // ReSharper disable once InconsistentlySynchronizedField
        while (_packetQueue.Count > _maxQueueLength)
        {
            var releaseCount = DatagramChannels.Length - _packetSenderSemaphore.CurrentCount;
            if (releaseCount > 0)
                _packetSenderSemaphore.Release(releaseCount); // there is some packet
            await _packetSentEvent.WaitAsync(1000); //Wait 1000 to prevent dead lock.
            if (_disposed) return;

            // check timeout
            if (FastDateTime.Now - dateTime > _datagramPacketTimeout)
                throw new TimeoutException("Could not send datagram packets.");
        }

        // add all packets to the queue
        lock (_packetQueue)
        {
            // ReSharper disable once PossibleMultipleEnumeration
            foreach (var ipPacket in ipPackets)
                _packetQueue.Enqueue(ipPacket);

            var releaseCount = DatagramChannels.Length - _packetSenderSemaphore.CurrentCount;
            if (releaseCount > 0)
                _packetSenderSemaphore.Release(releaseCount); // there are some packets! 
        }

        // ReSharper disable once PossibleMultipleEnumeration
        if (VhLogger.IsDiagnoseMode)
            PacketUtil.LogPackets(ipPackets, "Packet sent to tunnel queue.");
    }

    private async Task SendPacketTask(IDatagramChannel channel)
    {
        var packets = new List<IPPacket>();

        // ** Warning: This is one of the most busy loop in the app. Performance is critical!
        try
        {
            while (channel.Connected && !channel.IsClosePending && !_disposed)
            {
                if (_disposed)
                    return;

                //only one thread can dequeue packets to let send buffer with sequential packets
                // dequeue available packets and add them to list in favor of buffer size
                lock (_packetQueue)
                {
                    var size = 0;
                    packets.Clear();
                    while (_packetQueue.TryPeek(out var ipPacket))
                    {
                        if (ipPacket == null) throw new Exception("Null packet should not be in the queue.");
                        var packetSize = ipPacket.TotalPacketLength;

                        // drop packet if it is larger than _mtuWithFragment
                        if (packetSize > _mtuWithFragment)
                        {
                            VhLogger.Instance.LogWarning($"Packet dropped! There is no channel to support this fragmented packet. Fragmented MTU: {_mtuWithFragment}, PacketLength: {ipPacket.TotalLength}, Packet: {PacketUtil.Format(ipPacket)}");
                            _packetQueue.TryDequeue(out ipPacket);
                            continue;
                        }

                        // drop packet if it is larger than _mtuNoFragment
                        if (packetSize > _mtuNoFragment && ipPacket is IPv4Packet { FragmentFlags: 2 })
                        {
                            VhLogger.Instance.LogWarning($"Packet dropped! There is no channel to support this non fragmented packet. NoFragmented MTU: {_mtuNoFragment}, Packet: {PacketUtil.Format(ipPacket)}");
                            _packetQueue.TryDequeue(out ipPacket);
                            var replyPacket = PacketUtil.CreatePacketTooBigReply(ipPacket, (ushort)_mtuNoFragment);
                            OnPacketReceived?.Invoke(this, new ChannelPacketReceivedEventArgs(new[] { replyPacket }, channel));
                            continue;
                        }

                        // just send this packet if it is bigger than _mtuNoFragment and there is no more packet in the buffer
                        // packets should be empty to decrease the chance of loosing the other packets by this packet
                        if (packetSize > _mtuNoFragment && !packets.Any() && _packetQueue.TryDequeue(out ipPacket))
                        {
                            packets.Add(ipPacket);
                            break;
                        }

                        // send other packets if this packet makes the buffer too big
                        if (packetSize + size > _mtuNoFragment)
                            break;

                        size += packetSize;
                        if (_packetQueue.TryDequeue(out ipPacket))
                            packets.Add(ipPacket);
                    }
                }

                // send selected packets
                if (packets.Count > 0)
                {
                    _packetSenderSemaphore.Release(); // lets the other do the rest (if any)
                    _packetSentEvent.Release();
                    await channel.SendPacketAsync(packets.ToArray());
                }
                // wait for next new packets
                else
                {
                    await _packetSenderSemaphore.WaitAsync();
                }
            } // while
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could not send some packets via a channel. PacketCount: {PacketCount}", packets.Count);
        }

        // make sure to remove the channel
        try
        {
            RemoveChannel(channel);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not remove a datagram channel.");
        }

        // lets the others do the rest of the job (if any)
        _packetSenderSemaphore.Release();
        _packetSentEvent.Release();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // make sure to call RemoveChannel to perform proper clean up such as setting _sentByteCount and _receivedByteCount 
        lock (_channelListLock)
        {
            foreach (var channel in _tcpProxyChannels.ToArray())
                RemoveChannel(channel);

            foreach (var channel in DatagramChannels.ToArray())
                RemoveChannel(channel);
        }

        lock (_packetQueue)
            _packetQueue.Clear();

        _speedMonitorTimer.Dispose();
        SendSpeed = 0;
        ReceiveSpeed = 0;

        // release worker threads
        _packetSenderSemaphore.Release(MaxDatagramChannelCount * 10); //make sure to release all semaphores
        _packetSentEvent.Release();
        _closePendingChannels.Dispose();
    }
}